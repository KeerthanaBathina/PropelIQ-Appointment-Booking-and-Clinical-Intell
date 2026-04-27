using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;

namespace UPACIP.Service.AI.AiCost;

/// <summary>
/// Checks daily AI cost totals against configured budget thresholds and emits
/// structured <c>LogCritical</c> admin alerts when a provider's spend exceeds
/// its limit (US_071 AC-2).
///
/// <para>
/// Scoped lifetime — resolves <see cref="ApplicationDbContext"/> within the
/// <see cref="AiCostAggregationJob"/> per-run scope.
/// </para>
///
/// <para>
/// No PII is included in any log output.  Alert fields: provider name, threshold
/// (USD), actual cost (USD), and percentage over budget.
/// </para>
/// </summary>
public sealed class AiCostAlertService : IAiCostAlertService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ApplicationDbContext         _dbContext;
    private readonly ILogger<AiCostAlertService>  _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public AiCostAlertService(
        ApplicationDbContext        dbContext,
        ILogger<AiCostAlertService> logger)
    {
        _dbContext = dbContext;
        _logger    = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IAiCostAlertService
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task CheckBudgetThresholdsAsync(DateOnly targetDate, CancellationToken ct = default)
    {
        // Sum daily costs per provider from the pre-aggregated summary table.
        var costsByProvider = await _dbContext.AiCostDailySummaries
            .AsNoTracking()
            .Where(s => s.SummaryDate == targetDate)
            .GroupBy(s => s.Provider)
            .Select(g => new
            {
                Provider   = g.Key,
                TotalCost  = g.Sum(s => s.TotalEstimatedCost),
            })
            .ToListAsync(ct);

        if (costsByProvider.Count == 0)
        {
            _logger.LogInformation(
                "AiCostAlertService: no daily summaries found for {Date} — skipping threshold check.",
                targetDate);
            return;
        }

        // Load budget configs for each provider that has data.
        var providers = costsByProvider.Select(c => c.Provider).ToList();
        var budgetConfigs = await _dbContext.AiCostBudgetConfigs
            .AsNoTracking()
            .Where(bc => providers.Contains(bc.Provider))
            .ToDictionaryAsync(bc => bc.Provider, ct);

        foreach (var providerCost in costsByProvider)
        {
            if (!budgetConfigs.TryGetValue(providerCost.Provider, out var config))
            {
                _logger.LogWarning(
                    "AiCostAlertService: no budget config found for provider {Provider} — skipping alert check.",
                    providerCost.Provider);
                continue;
            }

            if (!config.AlertEnabled)
                continue;

            if (providerCost.TotalCost > config.DailyBudgetThreshold)
            {
                await SendBudgetBreachAlertAsync(
                    config.Provider.ToString(),
                    config.DailyBudgetThreshold,
                    providerCost.TotalCost,
                    ct);
            }
        }
    }

    /// <inheritdoc/>
    public Task SendBudgetBreachAlertAsync(
        string            provider,
        decimal           threshold,
        decimal           actualCost,
        CancellationToken ct = default)
    {
        var percentageOver = threshold > 0m
            ? Math.Round((actualCost - threshold) / threshold * 100m, 2)
            : 0m;

        _logger.LogCritical(
            "AI_COST_BUDGET_BREACH — Provider={Provider} exceeded daily budget. " +
            "Threshold={Threshold:F6} USD, ActualCost={ActualCost:F6} USD, OverBudget={PercentageOver:F2}%.",
            provider, threshold, actualCost, percentageOver);

        return Task.CompletedTask;
    }
}
