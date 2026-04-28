namespace UPACIP.Service.Performance.Models;

/// <summary>
/// Configuration options for the performance monitoring framework
/// (US_081 task_001, AC-4). Bound from <c>appsettings.json</c> section
/// <c>"PerformanceMonitoring"</c>.
/// </summary>
public sealed class PerformanceOptions
{
    /// <summary>Configuration section key.</summary>
    public const string SectionName = "PerformanceMonitoring";

    /// <summary>
    /// SLA latency thresholds in milliseconds, keyed by operation type.
    /// An SLA breach alert is emitted when the P95 for an operation exceeds its threshold.
    /// Default values: Booking=2000ms, DocumentParsing=30000ms, MedicalCoding=5000ms.
    /// </summary>
    public Dictionary<string, int> SlaThresholdsMs { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Booking"]           = 2_000,
        ["DocumentParsing"]   = 30_000,
        ["MedicalCoding"]     = 5_000,
    };

    /// <summary>
    /// How often (in seconds) the background <c>PerformanceMonitoringService</c>
    /// evaluates SLA compliance. Default: 60 seconds.
    /// </summary>
    public int EvaluationIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Size of the sliding window (in minutes) over which P95 values are computed.
    /// Samples older than this window are excluded from percentile calculations.
    /// Default: 15 minutes.
    /// </summary>
    public int SlidingWindowMinutes { get; set; } = 15;

    /// <summary>
    /// Minimum time (in minutes) between duplicate alerts for the same operation type.
    /// Prevents alert flooding when an SLA is persistently breached.
    /// Default: 5 minutes.
    /// </summary>
    public int AlertCooldownMinutes { get; set; } = 5;

    /// <summary>
    /// When <see langword="true"/>, the <c>PerformanceInstrumentationMiddleware</c>
    /// creates hierarchical <see cref="System.Diagnostics.Activity"/> spans for each
    /// request (edge case — span-level APM tracing).  Default: <see langword="true"/>.
    /// </summary>
    public bool EnableSpanTracing { get; set; } = true;
}
