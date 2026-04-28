namespace UPACIP.Service.Performance.Models;

/// <summary>
/// Computed latency percentiles for a single operation type over the
/// current sliding window (US_081 task_001, AC-4).
/// </summary>
public sealed record OperationMetric
{
    /// <summary>Operation type name (e.g. <c>Booking</c>, <c>DocumentParsing</c>).</summary>
    public string OperationType { get; init; } = string.Empty;

    /// <summary>50th-percentile latency in milliseconds (median).</summary>
    public double P50Ms { get; init; }

    /// <summary>95th-percentile latency in milliseconds (SLA evaluation target).</summary>
    public double P95Ms { get; init; }

    /// <summary>99th-percentile latency in milliseconds (extreme-case indicator).</summary>
    public double P99Ms { get; init; }

    /// <summary>Number of samples used to compute the percentiles.</summary>
    public int SampleCount { get; init; }

    /// <summary>Start of the sliding window from which samples were taken (UTC).</summary>
    public DateTime WindowStart { get; init; }

    /// <summary>End of the sliding window (UTC, approximately <c>DateTime.UtcNow</c>).</summary>
    public DateTime WindowEnd { get; init; }

    /// <summary>
    /// <see langword="true"/> when <see cref="P95Ms"/> is within the configured SLA threshold
    /// for this operation type.
    /// </summary>
    public bool IsWithinSla { get; init; }
}
