using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.AI.ClinicalExtraction;

// ─────────────────────────────────────────────────────────────────────────────
// Per-entry confidence evaluation result
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Confidence evaluation result for a single extracted clinical data item
/// (US_046, AIR-010, AIR-Q07, AIR-Q08).
/// </summary>
public sealed record ConfidenceEntryResult
{
    /// <summary>The <c>ExtractedData</c> primary key, or a temporary correlation ID before persistence.</summary>
    public Guid CorrelationId { get; init; }

    /// <summary>Clinical category of the item.</summary>
    public DataType DataType { get; init; }

    /// <summary>Normalized value extracted from the document.</summary>
    public string? NormalizedValue { get; init; }

    /// <summary>Confidence score in [0.0, 1.0]. Null is treated as 0 per guardrails.json.</summary>
    public float? ConfidenceScore { get; init; }

    /// <summary>Effective score after null normalisation (null → 0.0).</summary>
    public float EffectiveScore => ConfidenceScore ?? 0f;

    /// <summary>True when <see cref="EffectiveScore"/> is below the 0.80 threshold (AC-1).</summary>
    public bool IsBelowThreshold => EffectiveScore < ConfidenceThresholdGate.Threshold;

    /// <summary>True when <see cref="ConfidenceScore"/> was null (must review regardless of threshold).</summary>
    public bool HasNullScore => ConfidenceScore is null;

    /// <summary>True when this entry must appear in the manual review queue.</summary>
    public bool RequiresManualReview => IsBelowThreshold || HasNullScore;
}

// ─────────────────────────────────────────────────────────────────────────────
// Aggregate confidence gate result (batch level)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Result of the confidence threshold evaluation for an entire extraction batch
/// (US_046 AC-1, AIR-010, AIR-Q07, AIR-Q08).
/// </summary>
public sealed record ConfidenceGateResult
{
    /// <summary>Correlation ID for the consolidation run or document batch.</summary>
    public Guid CorrelationId { get; init; }

    /// <summary>Per-entry evaluation results, ordered by data type then score ascending.</summary>
    public IReadOnlyList<ConfidenceEntryResult> Entries { get; init; } = [];

    /// <summary>Mean confidence across all entries (null-scores count as 0).</summary>
    public float MeanConfidence { get; init; }

    /// <summary>
    /// True when the mean confidence across the batch is below the 0.80 threshold, OR
    /// when at least one entry has a null confidence score (AC-1).
    /// When true the entire batch is sent to the manual review queue.
    /// </summary>
    public bool RequiresBatchManualReview { get; init; }

    /// <summary>Individual entries that are below threshold or have null scores.</summary>
    public IReadOnlyList<ConfidenceEntryResult> FlaggedEntries => Entries.Where(e => e.RequiresManualReview).ToList();

    /// <summary>Count of entries below threshold or with null score.</summary>
    public int FlaggedCount => FlaggedEntries.Count;

    /// <summary>Total entry count in the batch.</summary>
    public int TotalCount => Entries.Count;

    /// <summary>Empty gate result (no entries evaluated).</summary>
    public static ConfidenceGateResult Empty(Guid correlationId) => new()
    {
        CorrelationId           = correlationId,
        Entries                 = [],
        MeanConfidence          = 1f,
        RequiresBatchManualReview = false,
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// Date plausibility result
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Result of the AI-assisted date plausibility validation
/// (US_046 AC-2, AIR-S10, edge case).
/// </summary>
public sealed record DatePlausibilityResult
{
    /// <summary>Validation run ID for correlation.</summary>
    public Guid ValidationId { get; init; }

    /// <summary>All violations detected (empty when dates are plausible).</summary>
    public IReadOnlyList<DatePlausibilityViolation> Violations { get; init; } = [];

    /// <summary>True when any violation with <see cref="DatePlausibilityViolation.IsIncompleteDate"/> = false was found.</summary>
    public bool HasChronologicalViolations => Violations.Any(v => !v.IsIncompleteDate);

    /// <summary>True when any incomplete-date flag was raised.</summary>
    public bool HasIncompleteDates => Violations.Any(v => v.IsIncompleteDate);

    /// <summary>Empty result (no violations).</summary>
    public static DatePlausibilityResult Empty(Guid validationId) => new()
    {
        ValidationId = validationId,
        Violations   = [],
    };
}

/// <summary>A single date plausibility violation from the AI validation pass.</summary>
public sealed record DatePlausibilityViolation
{
    /// <summary>Correlation ID of the <c>ExtractedData</c> row that carries the offending date.</summary>
    public Guid ExtractedDataId { get; init; }

    /// <summary>Clinical category of the violating entry.</summary>
    public DataType DataType { get; init; }

    /// <summary>
    /// Human-readable explanation (AC-2). Contains specific dates and clinical entity names.
    /// No PII (patient name / contact info) is included.
    /// </summary>
    public string Explanation { get; init; } = string.Empty;

    /// <summary>Severity band: low | medium | high.</summary>
    public string Severity { get; init; } = "medium";

    /// <summary>True when this entry represents an incomplete/partial date rather than a true violation.</summary>
    public bool IsIncompleteDate { get; init; }
}
