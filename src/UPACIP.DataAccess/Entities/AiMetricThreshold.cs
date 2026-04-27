namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Configurable alert threshold definition per AI metric (US_072 task_001, AC-4).
///
/// <para>
/// Each row defines the target and warning thresholds for a named metric.
/// The <see cref="MetricName"/> column carries a unique constraint so thresholds
/// can be looked up directly by metric name without ambiguity.
/// </para>
///
/// <para>
/// Seed data should include thresholds for all <see cref="AiMetricType"/> values
/// (CodingAgreement target: 98%, ExtractionPrecision/Recall target: 95%) and all
/// <see cref="AiOperationType"/> latency SLAs (Intake P95: 1000 ms, etc.).
/// </para>
/// </summary>
public sealed class AiMetricThreshold : BaseEntity
{
    /// <summary>
    /// Unique metric identifier used to correlate thresholds with metric records.
    /// For accuracy metrics this mirrors the <c>AiMetricType</c> enum name.
    /// For latency metrics this mirrors the <c>AiOperationType</c> enum name.
    /// Max length 100 characters.
    /// </summary>
    public string MetricName { get; set; } = string.Empty;

    /// <summary>
    /// The target threshold value that the metric must meet or exceed.
    /// For accuracy metrics: percentage (e.g., 98.0 for 98%).
    /// For latency metrics: milliseconds (e.g., 1000 for 1 s P95).
    /// </summary>
    public double TargetValue { get; set; }

    /// <summary>
    /// Warning threshold below the target at which a warning-level alert is raised.
    /// Must be less than <see cref="TargetValue"/> for accuracy metrics or greater for latency metrics.
    /// </summary>
    public double WarningValue { get; set; }

    /// <summary>
    /// <c>true</c> when this threshold is active and should be evaluated during the daily run.
    /// Disabled thresholds are skipped without deletion, supporting soft-disable workflows.
    /// </summary>
    public bool IsEnabled { get; set; } = true;
}
