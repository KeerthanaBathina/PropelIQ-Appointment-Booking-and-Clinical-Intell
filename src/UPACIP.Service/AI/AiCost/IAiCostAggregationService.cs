using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.AI.AiCost;

/// <summary>
/// Aggregates raw AI request log entries into daily cost summaries and exposes
/// a lightweight running-cost query backed by a Redis cache-aside read (US_071 AC-1).
/// </summary>
public interface IAiCostAggregationService
{
    /// <summary>
    /// Groups <c>AiRequestLog</c> rows for <paramref name="targetDate"/> by
    /// (Provider, RequestType), recalculates cost for any approximate entries
    /// using the rate card stored in <c>AiCostBudgetConfig</c>, and upserts
    /// <c>AiCostDailySummary</c> records (US_071 AC-1, edge case: approximate).
    /// </summary>
    Task AggregateDailyCostsAsync(DateOnly targetDate, CancellationToken ct = default);

    /// <summary>
    /// Returns the sum of <c>TotalEstimatedCost</c> for the given provider on today's
    /// UTC date.  Reads from a Redis cache key with a 5-minute TTL; falls back to a
    /// direct DB query on cache miss or Redis unavailability (NFR-030).
    /// </summary>
    Task<decimal> GetRunningDailyCostAsync(AiProvider provider, CancellationToken ct = default);
}
