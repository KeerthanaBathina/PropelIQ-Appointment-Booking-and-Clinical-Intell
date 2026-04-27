namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Pre-aggregated daily hallucination rate metric for AI-generated medical justifications
/// (US_074 task_002, AC-1, AC-2, AIR-Q06).
///
/// <para>
/// One row per calendar day, produced by <c>HallucinationAggregationJob</c>.
/// A unique index on <see cref="MetricDate"/> prevents duplicate daily entries and allows
/// safe upserts.
/// </para>
///
/// <para>
/// <see cref="HallucinationRate"/> = <see cref="HallucinationCount"/> / <see cref="TotalVerified"/>
/// expressed as a 0–1 decimal.  When rate exceeds <see cref="TargetRate"/> (0.05),
/// the aggregation job generates a <see cref="HallucinationAlert"/>.
/// </para>
/// </summary>
public sealed class HallucinationMetric : BaseEntity
{
    /// <summary>
    /// The UTC calendar date this metric row covers (date portion only; time is midnight UTC).
    /// Unique — enforced by <c>ix_hallucination_metrics_metric_date</c> unique index.
    /// </summary>
    public DateTime MetricDate { get; set; }

    /// <summary>Total number of AI justifications verified by staff on <see cref="MetricDate"/>.</summary>
    public int TotalVerified { get; set; }

    /// <summary>
    /// Number of verified justifications classified as
    /// <see cref="UPACIP.DataAccess.Enums.SourceSupportStatus.Unsupported"/> (hallucinations).
    /// </summary>
    public int HallucinationCount { get; set; }

    /// <summary>
    /// Number of verified justifications classified as
    /// <see cref="UPACIP.DataAccess.Enums.SourceSupportStatus.PartiallySupported"/>.
    /// Informational — not included in the hallucination rate calculation.
    /// </summary>
    public int PartiallySupportedCount { get; set; }

    /// <summary>
    /// Calculated hallucination rate for the day: <c>HallucinationCount / TotalVerified</c>.
    /// Stored as 0–1 decimal (e.g., 0.04 = 4 %).  Set to 0.0 when <see cref="TotalVerified"/> is 0.
    /// </summary>
    public double HallucinationRate { get; set; }

    /// <summary>
    /// Target hallucination rate threshold — <c>0.05</c> (5 %) per AC-2.
    /// Stored per-row so it can be changed without recalculating historical rows.
    /// </summary>
    public double TargetRate { get; set; } = 0.05;
}
