using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Daily aggregated AI accuracy metric snapshot (US_072 task_001, AC-1, AC-2).
///
/// <para>
/// One row per (<see cref="MetricDate"/>, <see cref="MetricType"/>) combination,
/// upserted by the metrics aggregation service (task_002).  A composite unique index
/// prevents duplicate daily entries for the same metric type.
/// </para>
///
/// <para>
/// <see cref="Value"/> is stored as a percentage in the range [0, 100].
/// <see cref="SampleSize"/> must reach the minimum threshold before the metric is
/// considered statistically meaningful (edge case: "Insufficient data").
/// </para>
/// </summary>
public sealed class AiAccuracyMetric : BaseEntity
{
    /// <summary>
    /// Calendar date (UTC) for which this metric was aggregated.
    /// Combined with <see cref="MetricType"/> to form the composite unique key.
    /// </summary>
    public DateTime MetricDate { get; set; }

    /// <summary>
    /// The type of accuracy metric represented by this row
    /// (e.g., <see cref="AiMetricType.CodingAgreement"/>,
    /// <see cref="AiMetricType.ExtractionPrecision"/>).
    /// </summary>
    public AiMetricType MetricType { get; set; }

    /// <summary>
    /// Measured accuracy value as a percentage in the range [0, 100].
    /// For example, 98.5 represents 98.5% agreement rate.
    /// </summary>
    public double Value { get; set; }

    /// <summary>
    /// Number of individual items included in the accuracy calculation.
    /// Values below the minimum threshold (e.g., 50) result in an "Insufficient data" state.
    /// </summary>
    public int SampleSize { get; set; }

    /// <summary>
    /// The configured target threshold for this metric type as a percentage.
    /// Used by the alert generation service to detect threshold breaches (AC-4).
    /// Example: 98.0 for CodingAgreement (AIR-Q01), 95.0 for ExtractionPrecision/Recall (AIR-Q02).
    /// </summary>
    public double TargetValue { get; set; }
}
