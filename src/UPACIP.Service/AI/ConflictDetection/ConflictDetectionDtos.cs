namespace UPACIP.Service.AI.ConflictDetection;

/// <summary>
/// Represents a single clinical conflict detected by the AI conflict detection service
/// (US_043, AIR-S09, AIR-S10, AIR-Q07).
///
/// Each conflict pairs two <see cref="ExtractedDataId"/> values that contradict each other,
/// classifies the conflict type and severity, and provides the LLM's reasoning for audit purposes.
/// The <see cref="RequiresUrgentReview"/> flag is set automatically for <see cref="ConflictSeverity.Critical"/>
/// conflicts and triggers an immediate staff notification (AIR-S09).
/// </summary>
public sealed record DetectedConflict
{
    /// <summary>
    /// Conflict type as a string so new types can be added without a migration.
    /// Known values: "MedicationContraindication", "ConflictingDiagnosis",
    /// "ChronologicallyImplausible", "Duplicate".
    /// </summary>
    public string ConflictType { get; init; } = string.Empty;

    /// <summary>Severity band of this conflict (Critical, High, Medium, Low).</summary>
    public ConflictSeverity Severity { get; init; }

    /// <summary>
    /// Primary ExtractedData row involved in the conflict.
    /// May be <see cref="Guid.Empty"/> when the LLM could not map the conflict to a specific row.
    /// </summary>
    public Guid DataPointAId { get; init; }

    /// <summary>
    /// Secondary ExtractedData row that conflicts with <see cref="DataPointAId"/>.
    /// May be <see cref="Guid.Empty"/> for single-row chronological plausibility issues.
    /// </summary>
    public Guid DataPointBId { get; init; }

    /// <summary>
    /// LLM-generated plain-English explanation of the conflict (audit trail, AIR-S04).
    /// PII is not included — only clinical entity names and conflict logic.
    /// </summary>
    public string Reasoning { get; init; } = string.Empty;

    /// <summary>
    /// True for <see cref="ConflictSeverity.Critical"/> conflicts that must be reviewed
    /// immediately (medication contraindications — AIR-S09).
    /// </summary>
    public bool RequiresUrgentReview { get; init; }

    /// <summary>LLM confidence score for this conflict in the range [0.0, 1.0].</summary>
    public float Confidence { get; init; }

    /// <summary>
    /// UUIDs of all additional <c>ExtractedData</c> rows beyond the primary pair when 3 or more
    /// source documents contribute to the same conflict (AIR-007, Edge Case — 3+ document conflicts).
    ///
    /// Empty for standard pairwise conflicts. When populated, the staff review UI renders ALL
    /// listed source citations in the comparison view — not just the first two found.
    /// </summary>
    public IReadOnlyList<Guid> AdditionalSourceIds { get; init; } = [];
}

/// <summary>
/// Envelope result returned by <see cref="IConflictDetectionService.DetectConflictsAsync"/>
/// (US_043, AIR-Q07).
///
/// Contains the complete list of detected conflicts and aggregate statistics used by
/// <see cref="ConsolidationService"/> to populate the <c>conflicts_detected_count</c>
/// in the <c>PatientProfileVersion</c> snapshot.
/// </summary>
public sealed record ConflictAnalysisResult
{
    /// <summary>True when at least one conflict was detected.</summary>
    public bool ConflictsDetected { get; init; }

    /// <summary>Total number of conflicts detected across all severity bands.</summary>
    public int ConflictCount { get; init; }

    /// <summary>Number of Critical-severity conflicts requiring urgent review (AIR-S09).</summary>
    public int CriticalCount { get; init; }

    /// <summary>Overall confidence score for the conflict analysis in [0.0, 1.0].</summary>
    public float AnalysisConfidence { get; init; }

    /// <summary>Ordered list of detected conflicts, Critical first.</summary>
    public IReadOnlyList<DetectedConflict> Conflicts { get; init; } = [];

    /// <summary>
    /// Which AI model provider produced this result ("openai", "anthropic", or "fallback").
    /// Used for audit logging and provider performance tracking.
    /// </summary>
    public string Provider { get; init; } = string.Empty;

    /// <summary>Wall-clock duration of the AI call in milliseconds.</summary>
    public long DurationMs { get; init; }

    /// <summary>True when the response was produced by the fallback path (no AI call made).</summary>
    public bool IsFallback { get; init; }

    /// <summary>
    /// True when the overall <see cref="AnalysisConfidence"/> is below the 0.80 manual-review
    /// threshold, indicating the AI was not sufficiently confident and the entire batch must
    /// be flagged for manual staff verification (AC-4, AIR-010, AIR-Q07).
    /// </summary>
    public bool RequiresManualVerification { get; init; }

    /// <summary>Returns an empty, safe result used when all AI providers fail.</summary>
    public static ConflictAnalysisResult Empty(long durationMs = 0) => new()
    {
        ConflictsDetected          = false,
        ConflictCount              = 0,
        CriticalCount              = 0,
        AnalysisConfidence         = 0,
        Conflicts                  = [],
        Provider                   = "fallback",
        DurationMs                 = durationMs,
        IsFallback                 = true,
        RequiresManualVerification = false,
    };
}
