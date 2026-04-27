using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Caching;

namespace UPACIP.Service.AI.AiCost;

/// <summary>
/// Aggregates raw <see cref="AiRequestLog"/> entries into daily cost summaries and exposes
/// a Redis-backed running-cost query for real-time budget monitoring (US_071 AC-1, AC-2).
///
/// <para>
/// Scoped lifetime — resolves <see cref="ApplicationDbContext"/> and <see cref="ICacheService"/>
/// within the <see cref="AiCostAggregationJob"/> per-run scope.
/// </para>
///
/// Edge case: when <see cref="AiCostSource.Approximate"/> entries are present the cost is
/// recalculated from the rate card stored in <see cref="AiCostBudgetConfig"/>
/// (<c>CostPer1kInputTokens</c> / <c>CostPer1kOutputTokens</c>) and the result is still
/// flagged via <c>ApproximateRequestCount</c> on the summary row.
/// </summary>
public sealed class AiCostAggregationService : IAiCostAggregationService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly TimeSpan RunningCostCacheTtl = TimeSpan.FromMinutes(5);

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ApplicationDbContext              _dbContext;
    private readonly ICacheService                     _cache;
    private readonly ILogger<AiCostAggregationService> _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public AiCostAggregationService(
        ApplicationDbContext              dbContext,
        ICacheService                     cache,
        ILogger<AiCostAggregationService> logger)
    {
        _dbContext = dbContext;
        _cache     = cache;
        _logger    = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IAiCostAggregationService
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task AggregateDailyCostsAsync(DateOnly targetDate, CancellationToken ct = default)
    {
        var startOfDay = DateTime.SpecifyKind(targetDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var endOfDay   = startOfDay.AddDays(1);

        // Load all provider rate cards upfront (small table — one row per provider).
        var rateCards = await _dbContext.AiCostBudgetConfigs
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Provider, ct);

        // Load all raw logs for the target day.
        var logs = await _dbContext.AiRequestLogs
            .Where(r => r.CreatedAt >= startOfDay && r.CreatedAt < endOfDay)
            .ToListAsync(ct);

        if (logs.Count == 0)
        {
            _logger.LogInformation(
                "AiCostAggregationService: no AI request logs found for {Date}.", targetDate);
            return;
        }

        // Group by (Provider, RequestType) and compute per-partition totals.
        var groups = logs
            .GroupBy(r => (r.Provider, r.RequestType))
            .Select(g =>
            {
                long    totalInput   = g.Sum(r => (long)r.InputTokens);
                long    totalOutput  = g.Sum(r => (long)r.OutputTokens);
                int     totalCount   = g.Count();
                int     approxCount  = g.Count(r => r.CostSource == AiCostSource.Approximate);
                decimal totalCost    = 0m;

                // Recalculate approximate entries from rate card; pass through actual costs.
                foreach (var entry in g)
                {
                    if (entry.CostSource == AiCostSource.Actual)
                    {
                        totalCost += entry.EstimatedCost;
                    }
                    else if (rateCards.TryGetValue(entry.Provider, out var rc))
                    {
                        totalCost += (entry.InputTokens  * rc.CostPer1kInputTokens  / 1_000m)
                                   + (entry.OutputTokens * rc.CostPer1kOutputTokens / 1_000m);
                    }
                    else
                    {
                        // No rate card available — use stored estimate as fallback.
                        totalCost += entry.EstimatedCost;
                    }
                }

                return new
                {
                    Provider                = g.Key.Provider,
                    RequestType             = g.Key.RequestType,
                    TotalInputTokens        = totalInput,
                    TotalOutputTokens       = totalOutput,
                    TotalEstimatedCost      = totalCost,
                    RequestCount            = totalCount,
                    ApproximateRequestCount = approxCount,
                };
            })
            .ToList();

        // Upsert AiCostDailySummary for each (Provider, RequestType) group.
        foreach (var g in groups)
        {
            var existing = await _dbContext.AiCostDailySummaries
                .FirstOrDefaultAsync(s =>
                    s.SummaryDate == targetDate &&
                    s.Provider    == g.Provider &&
                    s.RequestType == g.RequestType, ct);

            if (existing is null)
            {
                _dbContext.AiCostDailySummaries.Add(new AiCostDailySummary
                {
                    SummaryDate             = targetDate,
                    Provider                = g.Provider,
                    RequestType             = g.RequestType,
                    TotalInputTokens        = g.TotalInputTokens,
                    TotalOutputTokens       = g.TotalOutputTokens,
                    TotalEstimatedCost      = g.TotalEstimatedCost,
                    RequestCount            = g.RequestCount,
                    ApproximateRequestCount = g.ApproximateRequestCount,
                });
            }
            else
            {
                existing.TotalInputTokens        = g.TotalInputTokens;
                existing.TotalOutputTokens       = g.TotalOutputTokens;
                existing.TotalEstimatedCost      = g.TotalEstimatedCost;
                existing.RequestCount            = g.RequestCount;
                existing.ApproximateRequestCount = g.ApproximateRequestCount;
                existing.UpdatedAt               = DateTime.UtcNow;
            }
        }

        await _dbContext.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AiCostAggregationService: aggregated {GroupCount} partition(s) for {Date}.",
            groups.Count, targetDate);
    }

    /// <inheritdoc/>
    public async Task<decimal> GetRunningDailyCostAsync(AiProvider provider, CancellationToken ct = default)
    {
        var today    = DateOnly.FromDateTime(DateTime.UtcNow);
        var cacheKey = $"ai-cost:daily:{provider.ToString().ToLowerInvariant()}:{today:yyyy-MM-dd}";

        var cached = await _cache.GetAsync<CostCacheEntry>(cacheKey, ct);
        if (cached is not null)
            return cached.Amount;

        // Cache miss — query pre-aggregated daily summaries (O(log n) index seek).
        var total = await _dbContext.AiCostDailySummaries
            .AsNoTracking()
            .Where(s => s.SummaryDate == today && s.Provider == provider)
            .SumAsync(s => s.TotalEstimatedCost, ct);

        // Write-through — best-effort; ICacheService swallows Redis errors.
        await _cache.SetAsync(cacheKey, new CostCacheEntry(total), RunningCostCacheTtl, ct);

        return total;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Thin wrapper so the decimal amount can satisfy the <c>T : class</c> constraint.</summary>
    private sealed record CostCacheEntry(decimal Amount);
}
