using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Daily aggregated AI latency percentile metrics (US_072 task_001, AC-3).
///
/// <para>
/// One row per (<see cref="MetricDate"/>, <see cref="OperationType"/>) combination,
/// upserted by the metrics aggregation service (task_002).  A composite unique index
/// prevents duplicate daily entries for the same operation type.
/// </para>
///
/// <para>
/// Latency values are stored in milliseconds.  The <see cref="TargetP95Milliseconds"/>
/// column captures the configured SLA target so alert generation does not require a
/// separate threshold lookup for latency metrics.
/// </para>
/// </summary>
public sealed class AiLatencyMetric : BaseEntity
{
    /// <summary>
    /// Calendar date (UTC) for which this latency metric was aggregated.
    /// Combined with <see cref="OperationType"/> to form the composite unique key.
    /// </summary>
    public DateTime MetricDate { get; set; }

    /// <summary>
    /// The AI operation type whose latency is captured by this row
    /// (e.g., <see cref="AiOperationType.Intake"/>, <see cref="AiOperationType.DocumentParsing"/>).
    /// </summary>
    public AiOperationType OperationType { get; set; }

    /// <summary>50th-percentile (median) latency in milliseconds for this operation on this day.</summary>
    public double P50Milliseconds { get; set; }

    /// <summary>95th-percentile latency in milliseconds for this operation on this day.</summary>
    public double P95Milliseconds { get; set; }

    /// <summary>
    /// Configured P95 SLA target in milliseconds for this operation type (AC-3).
    /// Example: 1000 ms for Intake, 30000 ms for DocumentParsing, 5000 ms for MedicalCoding.
    /// </summary>
    public double TargetP95Milliseconds { get; set; }

    /// <summary>
    /// Number of individual operation invocations included in the percentile calculation.
    /// Values below the minimum threshold indicate insufficient data for statistical reliability.
    /// </summary>
    public int SampleSize { get; set; }
}
