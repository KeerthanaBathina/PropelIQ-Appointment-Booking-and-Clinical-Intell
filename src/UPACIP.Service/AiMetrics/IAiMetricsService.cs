using UPACIP.Service.AiMetrics.Dtos;

namespace UPACIP.Service.AiMetrics;

/// <summary>
/// Service contract for AI performance metrics aggregation, retrieval, and alert
/// management for the AI monitoring dashboard (US_072, AC-1 through AC-4).
/// </summary>
public interface IAiMetricsService
{
    /// <summary>
    /// Returns the current accuracy and latency summary for the AI monitoring dashboard
    /// (US_072 AC-1, AC-2, AC-3).
    /// <para>
    /// Loads the most recent persisted <c>AiAccuracyMetric</c> and <c>AiLatencyMetric</c>
    /// rows for each type.  When a metric's sample size is below 30 the caller must
    /// display "Insufficient data" (edge case).
    /// </para>
    /// </summary>
    Task<AiMetricsSummaryDto> GetCurrentSummaryAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns time-series data points for the specified metric type over a date range
    /// (US_072 edge case: daily/weekly/monthly views with date range selectors).
    /// </summary>
    /// <param name="metricType">
    /// Metric type name — one of "CodingAgreement", "ExtractionPrecision", "ExtractionRecall".
    /// Latency metrics are not supported on this endpoint; use the summary endpoint instead.
    /// </param>
    /// <param name="startDate">Inclusive range start (UTC).</param>
    /// <param name="endDate">Inclusive range end (UTC).</param>
    /// <param name="granularity">
    /// Aggregation granularity: "daily" (default), "weekly", or "monthly".
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<AiMetricsTimeSeriesDto> GetTimeSeriesAsync(
        string           metricType,
        DateTime         startDate,
        DateTime         endDate,
        string           granularity,
        CancellationToken ct = default);

    /// <summary>
    /// Returns all active (unacknowledged) metric alerts (US_072 AC-4).
    /// </summary>
    Task<IReadOnlyList<AiMetricAlertDto>> GetActiveAlertsAsync(CancellationToken ct = default);

    /// <summary>
    /// Marks the specified alert as acknowledged by the given admin user (US_072 AC-4).
    /// </summary>
    /// <param name="alertId">ID of the alert to acknowledge.</param>
    /// <param name="acknowledgedByUserId">ID of the admin user performing the acknowledgement.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if the alert was found and updated; <c>false</c> if not found.</returns>
    Task<bool> AcknowledgeAlertAsync(Guid alertId, Guid acknowledgedByUserId, CancellationToken ct = default);

    /// <summary>
    /// Executes the full daily aggregation pipeline: accuracy calculation from
    /// <c>MedicalCode</c>/<c>ExtractedData</c> entities, latency percentile aggregation from
    /// AI Gateway request logs, result persistence, and threshold-based alert generation (AC-4).
    /// <para>
    /// Invoked by <see cref="AiMetricsCalculationJob"/> on its 24-hour schedule.
    /// May also be called for ad-hoc recalculation.
    /// </para>
    /// </summary>
    /// <param name="targetDate">Date (UTC midnight) for which to run aggregation.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RunDailyAggregationAsync(DateTime targetDate, CancellationToken ct = default);
}
