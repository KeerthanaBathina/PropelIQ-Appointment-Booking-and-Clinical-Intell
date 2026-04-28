using UPACIP.Service.Performance.Models;

namespace UPACIP.Service.Performance;

/// <summary>
/// Contract for SLA compliance evaluation and P95 metric retrieval
/// (US_081 task_001, AC-4).
///
/// <para>
/// Computes P50/P95/P99 latency percentiles from the in-memory sliding-window
/// histogram maintained by <see cref="IPerformanceTracker"/>, then evaluates
/// each result against configured SLA thresholds.
/// </para>
/// </summary>
public interface ISlaMonitorService
{
    /// <summary>
    /// Computes current P50, P95, and P99 latency percentiles for all operation types
    /// that have at least 10 samples within the configured sliding window.
    /// </summary>
    /// <param name="ct">Propagates cancellation.</param>
    /// <returns>
    /// One <see cref="OperationMetric"/> per eligible operation type.
    /// </returns>
    Task<IReadOnlyList<OperationMetric>> GetCurrentMetricsAsync(CancellationToken ct = default);

    /// <summary>
    /// Evaluates all operation metrics against their configured SLA thresholds and returns
    /// breach alerts.  Respects the <c>AlertCooldownMinutes</c> setting to suppress duplicates.
    /// </summary>
    /// <param name="ct">Propagates cancellation.</param>
    /// <returns>
    /// List of <see cref="SlaAlert"/> instances (empty when all operations are within SLA).
    /// </returns>
    Task<IReadOnlyList<SlaAlert>> EvaluateSlaComplianceAsync(CancellationToken ct = default);
}
