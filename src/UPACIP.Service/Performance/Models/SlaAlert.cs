namespace UPACIP.Service.Performance.Models;

/// <summary>
/// Trend direction compared to the previous SLA evaluation cycle (US_081 task_001, AC-4).
/// </summary>
public enum TrendDirection
{
    /// <summary>P95 has decreased since the previous evaluation (latency improving).</summary>
    Improving = 0,

    /// <summary>P95 is unchanged or within noise margin (±5%) of the previous value.</summary>
    Stable = 1,

    /// <summary>P95 has increased since the previous evaluation (latency degrading).</summary>
    Degrading = 2,
}

/// <summary>
/// SLA breach alert generated when a computed P95 latency exceeds the configured threshold
/// for an operation type (US_081 task_001, AC-4).
/// </summary>
public sealed record SlaAlert
{
    /// <summary>Operation type that breached its SLA (e.g. <c>Booking</c>).</summary>
    public string OperationType { get; init; } = string.Empty;

    /// <summary>Computed P95 latency at the time of the alert (milliseconds).</summary>
    public double CurrentP95Ms { get; init; }

    /// <summary>Configured SLA threshold that was exceeded (milliseconds).</summary>
    public int ThresholdMs { get; init; }

    /// <summary>Trend direction compared to the previous evaluation cycle.</summary>
    public TrendDirection Trend { get; init; }

    /// <summary>UTC timestamp when the alert was generated.</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Human-readable alert description suitable for Serilog structured log message.
    /// Example: <c>Booking P95 latency 2450ms exceeds 2000ms threshold (Degrading)</c>.
    /// </summary>
    public string Message { get; init; } = string.Empty;
}
