using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Profile;

/// <summary>
/// Full 360° patient profile response DTO (US_043, AC-1, AC-3, FR-052).
///
/// Contains all four active clinical data categories — medications, diagnoses, procedures,
/// and allergies — plus patient summary, current consolidation version metadata, and
/// aggregate counts used by the staff review dashboard.
/// </summary>
public sealed record PatientProfile360Dto
{
    /// <summary>Patient entity primary key.</summary>
    public Guid PatientId { get; init; }

    /// <summary>Patient display name.</summary>
    public string PatientName { get; init; } = string.Empty;

    /// <summary>Patient date of birth (ISO-8601 YYYY-MM-DD).</summary>
    public DateOnly DateOfBirth { get; init; }

    /// <summary>Current profile version number; 0 when no consolidation has run yet.</summary>
    public int CurrentVersionNumber { get; init; }

    /// <summary>UTC timestamp of the most recent consolidation run. Null when never consolidated.</summary>
    public DateTime? LastConsolidatedAt { get; init; }

    /// <summary>Total number of data points flagged for staff review across all categories.</summary>
    public int PendingReviewCount { get; init; }

    /// <summary>
    /// Number of detected conflicts in the current version.
    /// Populated by IConflictDetectionService (US_043 task_004); returns 0 until that service is wired in.
    /// </summary>
    public int ConflictCount { get; init; }

    /// <summary>Merged medication entries from all parsed documents.</summary>
    public IReadOnlyList<ProfileDataPointDto> Medications { get; init; } = [];

    /// <summary>Merged diagnosis entries from all parsed documents.</summary>
    public IReadOnlyList<ProfileDataPointDto> Diagnoses { get; init; } = [];

    /// <summary>Merged procedure entries from all parsed documents.</summary>
    public IReadOnlyList<ProfileDataPointDto> Procedures { get; init; } = [];

    /// <summary>Merged allergy entries from all parsed documents.</summary>
    public IReadOnlyList<ProfileDataPointDto> Allergies { get; init; } = [];
}

/// <summary>
/// A single extracted clinical data point with source attribution and review metadata (AC-3, FR-052).
///
/// The <see cref="SourceDocumentId"/> and <see cref="ExtractionRegion"/> fields allow the UI
/// to present a clickable citation that links the data point to the exact document section
/// from which it was extracted.
/// </summary>
public sealed record ProfileDataPointDto
{
    /// <summary>ExtractedData primary key — use this to request the full source citation.</summary>
    public Guid ExtractedDataId { get; init; }

    /// <summary>Clinical data category (Medication, Diagnosis, Procedure, Allergy).</summary>
    public DataType DataType { get; init; }

    /// <summary>Normalized clinical value (e.g. "Metformin 500mg", "E11.9", "CPT 99213").</summary>
    public string? NormalizedValue { get; init; }

    /// <summary>Raw text extracted verbatim from the source document.</summary>
    public string? RawText { get; init; }

    /// <summary>Unit of measure when applicable (e.g. "mg", "mmHg").</summary>
    public string? Unit { get; init; }

    /// <summary>Surrounding sentence or paragraph from the document for quick context.</summary>
    public string? SourceSnippet { get; init; }

    /// <summary>Model confidence score in the range [0.0, 1.0].</summary>
    public float ConfidenceScore { get; init; }

    /// <summary>FK to the source <see cref="ClinicalDocument"/>.</summary>
    public Guid SourceDocumentId { get; init; }

    /// <summary>Original file name of the source document (display only).</summary>
    public string SourceDocumentName { get; init; } = string.Empty;

    /// <summary>Document category (LabResult, Prescription, ClinicalNote, ImagingReport).</summary>
    public DocumentCategory SourceDocumentCategory { get; init; }

    /// <summary>Page number within the source document where this data point was found.</summary>
    public int PageNumber { get; init; }

    /// <summary>Coarse region within the page (e.g. "header", "body", "table").</summary>
    public string ExtractionRegion { get; init; } = string.Empty;

    /// <summary>AI model name and version string used to produce this extraction.</summary>
    public string SourceAttribution { get; init; } = string.Empty;

    /// <summary>True when the confidence score is below the auto-approve threshold.</summary>
    public bool FlaggedForReview { get; init; }

    /// <summary>Current review state of this data point (Pending, Accepted, Rejected).</summary>
    public string VerificationStatus { get; init; } = string.Empty;

    /// <summary>UTC timestamp when a staff member verified this data point. Null if unreviewed.</summary>
    public DateTime? VerifiedAtUtc { get; init; }
}

/// <summary>
/// Full source document citation for a single data point (AC-3, FR-052).
///
/// Returned by <c>GET /api/patients/{patientId}/profile/data-points/{extractedDataId}/citation</c>
/// and displayed in the staff profile view when a data point is selected.
/// </summary>
public sealed record SourceCitationDto
{
    /// <summary>ExtractedData primary key.</summary>
    public Guid ExtractedDataId { get; init; }

    /// <summary>FK to the source clinical document.</summary>
    public Guid DocumentId { get; init; }

    /// <summary>Original file name of the document (display only).</summary>
    public string DocumentName { get; init; } = string.Empty;

    /// <summary>Document category.</summary>
    public DocumentCategory DocumentCategory { get; init; }

    /// <summary>UTC timestamp when the document was uploaded.</summary>
    public DateTime UploadDate { get; init; }

    /// <summary>Page number within the document.</summary>
    public int PageNumber { get; init; }

    /// <summary>Coarse region within the page (e.g. "header", "body", "table").</summary>
    public string ExtractionRegion { get; init; } = string.Empty;

    /// <summary>Surrounding sentence or paragraph from which the data point was extracted.</summary>
    public string? SourceSnippet { get; init; }

    /// <summary>AI model name and version string used to produce this extraction.</summary>
    public string SourceAttribution { get; init; } = string.Empty;
}

/// <summary>
/// A single entry in the patient's profile version history (AC-2, FR-056).
///
/// Returned by the version-history and version-detail endpoints to let staff review
/// the full audit trail of consolidation events — when each ran, who triggered it,
/// which documents contributed, and what the consolidation type was.
/// </summary>
public sealed record VersionHistoryDto
{
    /// <summary>Per-patient monotonically increasing version counter.</summary>
    public int VersionNumber { get; init; }

    /// <summary>UTC timestamp when this version was created.</summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Display name of the staff user who triggered this consolidation.
    /// "Automated" when initiated by the background pipeline (ConsolidatedByUserId is null).
    /// </summary>
    public string ConsolidatedByUserName { get; init; } = string.Empty;

    /// <summary>Whether this was an initial or incremental consolidation.</summary>
    public string ConsolidationType { get; init; } = string.Empty;

    /// <summary>Number of source documents that contributed to this version.</summary>
    public int SourceDocumentCount { get; init; }

    /// <summary>
    /// JSONB data delta snapshot for this version.
    /// Null for versions with no net change in extracted data.
    /// </summary>
    public string? DataSnapshot { get; init; }
}
