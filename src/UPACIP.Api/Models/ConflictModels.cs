using UPACIP.DataAccess.Enums;

namespace UPACIP.Api.Models;

/// <summary>
/// Summary row returned by GET /api/patients/{patientId}/conflicts (US_044, AC-2, AC-3, FR-053).
/// </summary>
public sealed record ConflictListDto
{
    /// <summary>ClinicalConflict primary key.</summary>
    public Guid ConflictId { get; init; }

    /// <summary>Category of the detected conflict.</summary>
    public ConflictType ConflictType { get; init; }

    /// <summary>Clinical severity driving review priority.</summary>
    public ConflictSeverity Severity { get; init; }

    /// <summary>Current lifecycle state of the conflict.</summary>
    public ConflictStatus Status { get; init; }

    /// <summary>True when this conflict is flagged as URGENT and shown at the top of the review queue (AC-3).</summary>
    public bool IsUrgent { get; init; }

    /// <summary>Patient display name.</summary>
    public string PatientName { get; init; } = string.Empty;

    /// <summary>AI-generated human-readable summary of the conflict.</summary>
    public string ConflictDescription { get; init; } = string.Empty;

    /// <summary>Number of source documents involved in the conflict (≥ 2; supports 3+ per Edge Case).</summary>
    public int SourceDocumentCount { get; init; }

    /// <summary>AI confidence score in [0.0, 1.0].</summary>
    public float AiConfidenceScore { get; init; }

    /// <summary>UTC timestamp when the conflict was first detected.</summary>
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// Source document citation for a single extracted data point involved in a conflict (AC-2, AC-3, AIR-007).
///
/// Enables the side-by-side comparison view to link each conflict data point directly
/// to the exact document section it was extracted from.
/// </summary>
public sealed record ConflictSourceCitationDto
{
    /// <summary>ClinicalDocument primary key.</summary>
    public Guid DocumentId { get; init; }

    /// <summary>Original filename of the source document.</summary>
    public string DocumentName { get; init; } = string.Empty;

    /// <summary>Clinical category of the source document (e.g. LabResult, Prescription).</summary>
    public string DocumentCategory { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the document was uploaded.</summary>
    public DateTime UploadDate { get; init; }

    /// <summary>ExtractedData primary key for this data point.</summary>
    public Guid ExtractedDataId { get; init; }

    /// <summary>Clinical data type of the extracted data point (e.g. Medication, Diagnosis).</summary>
    public string DataType { get; init; } = string.Empty;

    /// <summary>Normalized clinical value (e.g. "Metformin 500mg", "E11.9").</summary>
    public string? NormalizedValue { get; init; }

    /// <summary>Raw text as extracted from the source document verbatim.</summary>
    public string? RawText { get; init; }

    /// <summary>Unit of measure when applicable (e.g. "mg", "mmHg").</summary>
    public string? Unit { get; init; }

    /// <summary>Surrounding sentence or paragraph from the document for quick context review.</summary>
    public string? SourceSnippet { get; init; }

    /// <summary>AI model confidence score for this extracted data point in [0.0, 1.0].</summary>
    public float ConfidenceScore { get; init; }

    /// <summary>Model name and version string used to produce this extraction (AIR-007 attribution).</summary>
    public string SourceAttributionText { get; init; } = string.Empty;

    /// <summary>Page number within the source document where this data point was found.</summary>
    public int PageNumber { get; init; }

    /// <summary>Coarse region within the page (e.g. "header", "body", "table").</summary>
    public string ExtractionRegion { get; init; } = string.Empty;
}

/// <summary>
/// Full conflict detail returned by GET /api/patients/{patientId}/conflicts/{conflictId} (US_044, AC-2, AC-3, FR-053).
///
/// Includes all summary fields plus the AI explanation, full source citation chain for all
/// involved documents (supports 3+ per Edge Case), and resolution metadata when closed.
/// </summary>
public sealed record ConflictDetailDto
{
    /// <summary>ClinicalConflict primary key.</summary>
    public Guid ConflictId { get; init; }

    /// <summary>Patient primary key.</summary>
    public Guid PatientId { get; init; }

    /// <summary>Patient display name.</summary>
    public string PatientName { get; init; } = string.Empty;

    /// <summary>Category of the detected conflict.</summary>
    public ConflictType ConflictType { get; init; }

    /// <summary>Clinical severity driving review priority.</summary>
    public ConflictSeverity Severity { get; init; }

    /// <summary>Current lifecycle state of the conflict.</summary>
    public ConflictStatus Status { get; init; }

    /// <summary>True when this conflict is flagged as URGENT (AC-3).</summary>
    public bool IsUrgent { get; init; }

    /// <summary>AI-generated human-readable summary of the conflict.</summary>
    public string ConflictDescription { get; init; } = string.Empty;

    /// <summary>Detailed AI-generated explanation including clinical reasoning and supporting evidence.</summary>
    public string AiExplanation { get; init; } = string.Empty;

    /// <summary>AI confidence score in [0.0, 1.0].</summary>
    public float AiConfidenceScore { get; init; }

    /// <summary>
    /// Full citation chain for all source data points involved in the conflict (AC-2, AC-3, AIR-007).
    /// Contains one entry per involved ExtractedData row; supports 3+ sources (Edge Case).
    /// </summary>
    public IReadOnlyList<ConflictSourceCitationDto> SourceCitations { get; init; } = [];

    /// <summary>UTC timestamp when the conflict was first detected.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>Staff member who resolved or dismissed the conflict. Null while the conflict is open.</summary>
    public string? ResolvedByUserName { get; init; }

    /// <summary>Free-text notes recorded when the conflict was resolved or dismissed.</summary>
    public string? ResolutionNotes { get; init; }

    /// <summary>UTC timestamp when the conflict was closed. Null while the conflict is open.</summary>
    public DateTime? ResolvedAt { get; init; }

    /// <summary>Profile version that was current when this conflict was detected.</summary>
    public Guid? ProfileVersionId { get; init; }
}

/// <summary>
/// Request body for PUT /api/patients/{patientId}/conflicts/{conflictId}/resolve
/// and PUT /api/patients/{patientId}/conflicts/{conflictId}/dismiss (FR-053).
/// </summary>
public sealed record ResolveConflictDto
{
    /// <summary>
    /// Required explanation of why the conflict is being resolved or dismissed.
    /// Staff must document their clinical reasoning before closing a conflict.
    /// </summary>
    public string ResolutionNotes { get; init; } = string.Empty;
}

/// <summary>
/// Conflict count aggregation by type and severity returned by
/// GET /api/patients/{patientId}/conflicts/summary (US_044, FR-053).
///
/// Used by the staff dashboard to render conflict-count badges without loading full conflict lists.
/// </summary>
public sealed record ConflictSummaryDto
{
    /// <summary>Total number of open conflicts (Detected + UnderReview) for the patient.</summary>
    public int TotalOpen { get; init; }

    /// <summary>Number of open conflicts currently flagged as URGENT (AC-3).</summary>
    public int TotalUrgent { get; init; }

    /// <summary>Counts of open conflicts broken down by conflict type.</summary>
    public IReadOnlyDictionary<string, int> ByType { get; init; } =
        new Dictionary<string, int>();

    /// <summary>Counts of open conflicts broken down by severity.</summary>
    public IReadOnlyDictionary<string, int> BySeverity { get; init; } =
        new Dictionary<string, int>();
}

/// <summary>
/// Query parameters for filtering GET /api/patients/{patientId}/conflicts (US_044, FR-053).
/// </summary>
public sealed record ConflictQueryParameters
{
    /// <summary>Filter by conflict status (e.g. "detected", "under_review", "resolved", "dismissed").</summary>
    public string? Status { get; init; }

    /// <summary>Filter by conflict severity (e.g. "critical", "high", "medium", "low").</summary>
    public string? Severity { get; init; }

    /// <summary>Filter by conflict type (e.g. "medication_contraindication", "duplicate_diagnosis").</summary>
    public string? Type { get; init; }

    /// <summary>1-based page number (default: 1).</summary>
    public int Page { get; init; } = 1;

    /// <summary>Items per page (default: 20, max: 100).</summary>
    public int PageSize { get; init; } = 20;
}

/// <summary>
/// Generic paged list wrapper for conflict list responses.
/// </summary>
public sealed record ConflictPagedResult
{
    /// <summary>Conflict list for the requested page.</summary>
    public IReadOnlyList<ConflictListDto> Items { get; init; } = [];

    /// <summary>Total number of conflicts matching the filter (across all pages).</summary>
    public int TotalCount { get; init; }

    /// <summary>Current page index (1-based).</summary>
    public int Page { get; init; }

    /// <summary>Items per page.</summary>
    public int PageSize { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// US_045 — Resolution workflow API DTOs
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Request body for PUT /api/patients/{patientId}/conflicts/{conflictId}/select-value (US_045, AC-2).
///
/// Staff selects the authoritative data value from the conflicting sources.
/// The chosen <c>ExtractedData</c> ID must belong to the conflict's source list.
/// </summary>
public sealed record SelectValueRequestDto
{
    /// <summary>
    /// ID of the <c>ExtractedData</c> record the staff member identified as correct.
    /// Must appear in the conflict's <c>SourceExtractedDataIds</c> collection.
    /// </summary>
    public Guid SelectedExtractedDataId { get; init; }

    /// <summary>
    /// Staff rationale for selecting this value — required for audit trail (AC-2).
    /// Maximum 2 000 characters.
    /// </summary>
    public string ResolutionNotes { get; init; } = string.Empty;
}

/// <summary>
/// Request body for PUT /api/patients/{patientId}/conflicts/{conflictId}/both-valid (US_045, EC-2).
///
/// Staff confirms both conflicting values are clinically valid with different date attribution.
/// Both entries are preserved in the consolidated profile.
/// </summary>
public sealed record BothValidRequestDto
{
    /// <summary>
    /// Clinical explanation describing the distinct date context for each value.
    /// Required, minimum 10 characters, maximum 2 000 characters.
    /// </summary>
    public string Explanation { get; init; } = string.Empty;
}

/// <summary>
/// Response for GET /api/patients/{patientId}/conflicts/resolution-progress (US_045, EC-1, AC-4).
///
/// Allows the staff UI to display a progress indicator and resume partial work.
/// </summary>
public sealed record ResolutionProgressResponseDto
{
    /// <summary>ID of the patient this progress snapshot belongs to.</summary>
    public Guid PatientId { get; init; }

    /// <summary>Total number of detected conflicts for the patient (all statuses).</summary>
    public int TotalConflicts { get; init; }

    /// <summary>Number of conflicts already closed (Resolved or Dismissed).</summary>
    public int ResolvedCount { get; init; }

    /// <summary>Number of conflicts still open (Detected or UnderReview).</summary>
    public int RemainingCount { get; init; }

    /// <summary>Whole-number percentage resolved (0–100), rounded down.</summary>
    public int PercentComplete { get; init; }

    /// <summary>
    /// Current verification status of the latest <c>PatientProfileVersion</c>:
    /// <c>Unverified</c>, <c>PartiallyVerified</c>, or <c>Verified</c>.
    /// </summary>
    public string VerificationStatus { get; init; } = "Unverified";
}

/// <summary>
/// Response for GET /api/patients/{patientId}/profile/verification-status (US_045, AC-4).
///
/// Returns the current verification lifecycle state of the patient's latest profile version,
/// including staff attribution when the profile has been fully verified.
/// </summary>
public sealed record ProfileVerificationResponseDto
{
    /// <summary>
    /// Current verification status: <c>Unverified</c>, <c>PartiallyVerified</c>, or <c>Verified</c>.
    /// </summary>
    public string Status { get; init; } = "Unverified";

    /// <summary>
    /// ID of the staff member who completed final verification.
    /// <c>null</c> until all conflicts are resolved (AC-4).
    /// </summary>
    public Guid? VerifiedByUserId { get; init; }

    /// <summary>
    /// Display name of the staff member who completed final verification.
    /// <c>null</c> until verification is complete.
    /// </summary>
    public string? VerifiedByUserName { get; init; }

    /// <summary>
    /// UTC timestamp when the profile was transitioned to <c>Verified</c>.
    /// <c>null</c> until verification is complete (AC-4).
    /// </summary>
    public DateTimeOffset? VerifiedAt { get; init; }
}
