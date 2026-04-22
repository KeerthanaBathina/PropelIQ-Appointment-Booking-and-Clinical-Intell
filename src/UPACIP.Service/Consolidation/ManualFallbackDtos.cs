using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Consolidation;

// ─────────────────────────────────────────────────────────────────────────────
// Low-confidence results (US_046 AC-1, AC-2)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A single extracted-data row whose <c>ConfidenceScore</c> is below the 80% threshold,
/// returned by <see cref="IConsolidationConfidenceService"/> for the manual review form.
/// </summary>
public sealed record LowConfidenceItemDto
{
    /// <summary>Primary key of the <c>ExtractedData</c> row.</summary>
    public Guid ExtractedDataId { get; init; }

    /// <summary>Clinical category (Medication | Diagnosis | Procedure | Allergy).</summary>
    public DataType DataType { get; init; }

    /// <summary>Normalized value produced by the AI extraction pipeline (e.g. "E11.9", "Metformin 500mg").</summary>
    public string? NormalizedValue { get; init; }

    /// <summary>Raw text snippet as extracted from the source document.</summary>
    public string? RawText { get; init; }

    /// <summary>Unit of measure when applicable (e.g. "mg", "mmHg"). Null for non-scalar types.</summary>
    public string? Unit { get; init; }

    /// <summary>AI confidence score in [0.0, 1.0]; always below the 0.80 threshold for items in this list.</summary>
    public float ConfidenceScore { get; init; }

    /// <summary>Source document primary key.</summary>
    public Guid SourceDocumentId { get; init; }

    /// <summary>Original filename of the source document.</summary>
    public string SourceDocumentName { get; init; } = string.Empty;

    /// <summary>
    /// Clinical record date parsed from <c>ExtractedDataContent.Metadata["record_date"]</c>.
    /// ISO-8601 date string or null when no date was extracted.
    /// </summary>
    public string? RecordDate { get; init; }

    /// <summary>
    /// True when the extracted date value is partial (month/year-only or year-only).
    /// Staff must complete the date before the entry can be fully verified (edge case).
    /// </summary>
    public bool IsIncompleteDate { get; init; }

    /// <summary>
    /// Human-readable explanation of a detected chronological plausibility violation (AC-2).
    /// Null when no date conflict is found for this entry.
    /// </summary>
    public string? DateConflictExplanation { get; init; }
}

/// <summary>
/// Paginated result set returned by <c>GET /api/patients/{id}/profile/low-confidence</c>.
/// </summary>
public sealed record LowConfidenceResultDto
{
    /// <summary>Low-confidence items for manual review.</summary>
    public IReadOnlyList<LowConfidenceItemDto> Items { get; init; } = [];

    /// <summary>Total count of low-confidence items (before paging).</summary>
    public int TotalCount { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Manual verification submission (US_046 AC-3)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// A single confirmed entry in a manual verification submission batch.
/// </summary>
public sealed record ManualVerifyEntryDto
{
    /// <summary>The <c>ExtractedData</c> row being confirmed.</summary>
    public Guid ExtractedDataId { get; init; }

    /// <summary>
    /// Staff-corrected value. When non-null, the row's <c>NormalizedValue</c> is replaced
    /// and <c>VerificationStatus</c> is set to <c>ManualVerified</c> (corrected path).
    /// When null, the row is confirmed as-is.
    /// </summary>
    public string? CorrectedValue { get; init; }

    /// <summary>
    /// Staff notes explaining the change or confirmation reason.
    /// Stored in the <c>AuditLog</c> and required for all manual-verify actions (FR-093).
    /// Max 2000 characters.
    /// </summary>
    public string ResolutionNotes { get; init; } = string.Empty;
}

/// <summary>
/// Request body for <c>POST /api/patients/{id}/profile/manual-verify</c> (AC-3, NFR-034).
/// </summary>
public sealed record ManualVerifyRequestDto
{
    /// <summary>Entries to be confirmed or corrected.</summary>
    public IReadOnlyList<ManualVerifyEntryDto> Entries { get; init; } = [];
}

// ─────────────────────────────────────────────────────────────────────────────
// AI health check (US_046 AC-4, NFR-030)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Current AI provider availability status.
/// Returned by <c>GET /api/health/ai</c> and cached in Redis with a 5-minute TTL (NFR-030).
/// </summary>
public sealed record AiHealthStatusDto
{
    /// <summary>True when the AI gateway is reachable and accepting requests.</summary>
    public bool IsAvailable { get; init; }

    /// <summary>UTC timestamp when this status was last evaluated.</summary>
    public DateTimeOffset CheckedAt { get; init; }

    /// <summary>
    /// Human-readable reason for unavailability (e.g. circuit breaker open, connection timeout).
    /// Null when <see cref="IsAvailable"/> is true.
    /// </summary>
    public string? Reason { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Date validation violation (US_046 AC-2)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Describes a single chronological plausibility violation detected by <see cref="IDateValidationService"/>.
/// </summary>
public sealed record DateViolationDto
{
    /// <summary>Primary key of the <c>ExtractedData</c> row with the offending date.</summary>
    public Guid ExtractedDataId { get; init; }

    /// <summary>Clinical category of the violating entry.</summary>
    public DataType DataType { get; init; }

    /// <summary>Human-readable explanation of the violation (AC-2).</summary>
    public string Explanation { get; init; } = string.Empty;

    /// <summary>True when the date is partial (month/year or year only) rather than a full violation.</summary>
    public bool IsIncompleteDate { get; init; }
}
