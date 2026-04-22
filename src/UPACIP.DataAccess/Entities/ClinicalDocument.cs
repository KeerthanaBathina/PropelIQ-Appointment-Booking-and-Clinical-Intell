using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Represents a clinical document (lab result, prescription, imaging report, etc.)
/// uploaded by a staff member or patient.  Document content is stored on the file system
/// at <see cref="FilePath"/>; only metadata is persisted in PostgreSQL.
/// </summary>
public sealed class ClinicalDocument : BaseEntity
{
    /// <summary>FK to the <see cref="Patient"/> this document belongs to.</summary>
    public Guid PatientId { get; set; }

    /// <summary>Clinical category used to route the document to the correct AI parser.</summary>
    public DocumentCategory DocumentCategory { get; set; }

    /// <summary>
    /// Original filename submitted by the uploader (display only — never used in file I/O).
    /// Stored separately from the encrypted storage path to preserve attribution (US_038 AC-4).
    /// </summary>
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>
    /// MIME type reported by the uploader at ingestion time (e.g. <c>application/pdf</c>).
    /// Nullable for backward compatibility with pre-US_038 rows.
    /// Used by downstream AI parsers to select the correct extraction strategy.
    /// </summary>
    public string? ContentType { get; set; }

    /// <summary>
    /// Size of the original (pre-encryption) file in bytes.
    /// Nullable for backward compatibility with pre-US_038 rows.
    /// Used for storage auditing and UI display.
    /// </summary>
    public long? FileSizeBytes { get; set; }

    /// <summary>
    /// Relative path (relative to configured storage root) of the AES-256-encrypted payload.
    /// Must never be returned to clients without authorization checks (path-traversal guard).
    /// </summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>UTC timestamp when the document was uploaded.</summary>
    public DateTime UploadDate { get; set; } = DateTime.UtcNow;

    /// <summary>FK to the <see cref="ApplicationUser"/> who uploaded the document.</summary>
    public Guid UploaderUserId { get; set; }

    /// <summary>Current AI processing pipeline state for this document.</summary>
    public ProcessingStatus ProcessingStatus { get; set; } = ProcessingStatus.Uploaded;

    // -------------------------------------------------------------------------
    // Parsing lifecycle metadata (US_039 task_004, AC-4, AC-5, EC-1)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Number of parsing attempts made so far. Incremented by the dispatcher on each retry.
    /// Nullable for backward compatibility with documents uploaded before US_039.
    /// </summary>
    public int? ParseAttemptCount { get; set; }

    /// <summary>
    /// UTC timestamp when AI parsing last started (status → Processing).
    /// Supports worker-restart resume and SLA monitoring.
    /// </summary>
    public DateTime? ParseStartedAt { get; set; }

    /// <summary>
    /// UTC timestamp when parsing completed (success or terminal failure).
    /// Null until the first terminal transition (Completed or Failed).
    /// </summary>
    public DateTime? ParseCompletedAt { get; set; }

    /// <summary>
    /// UTC time after which this document should be re-attempted (exponential backoff schedule).
    /// Null if no retry is pending. Queried by the dispatcher to find due work after worker restart (EC-1).
    /// </summary>
    public DateTime? ParseNextAttemptAt { get; set; }

    /// <summary>
    /// Flag set when all retries are exhausted and the document requires staff intervention (AC-5).
    /// Persisted here for fast dashboard queries without joining to <see cref="ParseAttempts"/>.
    /// </summary>
    public bool RequiresManualReview { get; set; }

    /// <summary>
    /// Reason summary populated on terminal failure for staff review context (AC-5).
    /// Nullable: only populated when <see cref="RequiresManualReview"/> is true.
    /// </summary>
    public string? ManualReviewReason { get; set; }

    /// <summary>
    /// Structured extraction outcome for this document (US_040 EC-1, EC-2).
    /// Possible values: <c>extracted</c>, <c>no-data-extracted</c>, <c>unsupported-language</c>,
    /// <c>invalid-response</c>. Null until AI clinical extraction has run.
    /// Stored separately from <see cref="ProcessingStatus"/> so edge-case outcomes
    /// do not overload generic <c>failed</c> semantics.
    /// </summary>
    public string? ExtractionOutcome { get; set; }

    // -------------------------------------------------------------------------
    // Document version lineage (US_042 task_004 AC-2, AC-3)
    // -------------------------------------------------------------------------

    /// <summary>
    /// 1-based version number within the replacement chain for this document.
    /// Defaults to 1 for all initial uploads; incremented by 1 for each replacement.
    /// </summary>
    public int VersionNumber { get; set; } = 1;

    /// <summary>
    /// FK to the <see cref="ClinicalDocument"/> this version replaces.
    /// Null for the initial upload; set on replacement documents.
    /// Supports full version chain traversal for audit and rollback (EC-1).
    /// </summary>
    public Guid? PreviousVersionId { get; set; }

    /// <summary>
    /// True when this version has been superseded by a later replacement that has been
    /// successfully activated. False for the currently active version (AC-3).
    /// </summary>
    public bool IsSuperseded { get; set; } = false;

    /// <summary>
    /// UTC timestamp when this version was superseded (active version promoted).
    /// Null while still active. Set by <c>DocumentReplacementService</c> during activation (AC-3).
    /// </summary>
    public DateTime? SupersededAtUtc { get; set; }

    /// <summary>
    /// Flag signaling that the patient's consolidated profile needs to be rebuilt
    /// because a replacement document was successfully activated (EC-2).
    /// Consumed by the future EP-007 reconsolidation pipeline.
    /// </summary>
    public bool ReconsolidationNeeded { get; set; } = false;

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    public Patient Patient { get; set; } = null!;

    public ApplicationUser UploaderUser { get; set; } = null!;

    /// <summary>Document version this one replaces (self-reference). Null for initial versions.</summary>
    public ClinicalDocument? PreviousVersion { get; set; }

    /// <summary>Replacement documents that supersede this one.</summary>
    public ICollection<ClinicalDocument> ReplacementVersions { get; set; } = [];

    public ICollection<ExtractedData> ExtractedData { get; set; } = [];

    /// <summary>Per-attempt parsing failure records for retry scheduling and audit (US_039 AC-4, EC-1).</summary>
    public ICollection<DocumentParsingAttempt> ParseAttempts { get; set; } = [];
}
