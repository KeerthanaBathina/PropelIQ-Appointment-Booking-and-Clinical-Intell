namespace UPACIP.DataAccess.Entities;

/// <summary>
/// EF Core entity backing the <c>ab_metric_records</c> table.
/// Stores per-request AI performance metrics for each A/B experiment variant
/// (US_080 task_001, AC-2, AIR-O10).
///
/// <para>
/// Does NOT extend <see cref="BaseEntity"/> because metrics are immutable
/// append-only records and do not require an <c>UpdatedAt</c> field.
/// </para>
/// </summary>
public sealed class AbMetricRecordEntity
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to the owning <see cref="AbExperimentEntity"/>.</summary>
    public Guid ExperimentId { get; set; }

    /// <summary>
    /// Variant that served this request.
    /// Stored as string (<c>Control</c> | <c>Candidate</c>) for readability in raw SQL.
    /// </summary>
    public string Variant { get; set; } = "Control";

    /// <summary>
    /// Accuracy rating in [0, 1] assigned by a downstream validator.
    /// Null when not yet evaluated.
    /// </summary>
    public float? Accuracy { get; set; }

    /// <summary>End-to-end inference latency in milliseconds.</summary>
    public long LatencyMs { get; set; }

    /// <summary>Total tokens consumed (input + output).</summary>
    public int TokensUsed { get; set; }

    /// <summary>Estimated monetary cost for this request (USD).</summary>
    public decimal EstimatedCost { get; set; }

    /// <summary>Request type (e.g. <c>document-parsing</c>, <c>medical-coding</c>).</summary>
    public string RequestType { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the metric was recorded.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>Navigation to the parent experiment.</summary>
    public AbExperimentEntity? Experiment { get; set; }
}
