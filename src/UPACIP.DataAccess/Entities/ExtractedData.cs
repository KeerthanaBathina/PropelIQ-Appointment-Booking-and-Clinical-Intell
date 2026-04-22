using UPACIP.DataAccess.Entities.OwnedTypes;
using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Stores a single piece of clinical information extracted from a
/// <see cref="ClinicalDocument"/> by the AI parsing pipeline.
/// <see cref="DataContent"/> is stored as JSONB and holds the structured extraction result.
/// </summary>
public sealed class ExtractedData : BaseEntity
{
    /// <summary>FK to the source <see cref="ClinicalDocument"/>.</summary>
    public Guid DocumentId { get; set; }

    /// <summary>Clinical data category (medication, diagnosis, procedure, allergy).</summary>
    public DataType DataType { get; set; }

    /// <summary>
    /// Structured extraction result — stored as JSONB (data_content column).
    /// Null until the AI pipeline completes processing.
    /// </summary>
    public ExtractedDataContent? DataContent { get; set; }

    /// <summary>Model confidence score in the range [0.0, 1.0].</summary>
    public float ConfidenceScore { get; set; }

    /// <summary>
    /// Page number within the source document where this data point was found (AC-5 traceability).
    /// Defaults to 1 for single-page documents or when the model does not report a page.
    /// </summary>
    public int PageNumber { get; set; } = 1;

    /// <summary>
    /// Coarse region within the page where the data point was extracted
    /// (e.g. "header", "body", "footer", "table" — max 200 chars).
    /// Populated from the AI model's <c>extraction_region</c> field (AC-5 traceability).
    /// </summary>
    public string ExtractionRegion { get; set; } = string.Empty;

    /// <summary>Model name and version string used to produce this extraction.</summary>
    public string SourceAttribution { get; set; } = string.Empty;

    /// <summary>True when the confidence score is below the auto-approve threshold.</summary>
    public bool FlaggedForReview { get; set; }

    /// <summary>FK to the <see cref="ApplicationUser"/> who verified this extraction. Null if unreviewed.</summary>
    public Guid? VerifiedByUserId { get; set; }

    /// <summary>UTC timestamp when the verification was stamped (US_041 AC-4). Null while unreviewed.</summary>
    public DateTime? VerifiedAtUtc { get; set; }

    /// <summary>
    /// Current review state of this extracted row (US_041 AC-4, EC-2).
    /// Defaults to <see cref="VerificationStatus.Pending"/>.
    /// </summary>
    public VerificationStatus VerificationStatus { get; set; } = VerificationStatus.Pending;

    /// <summary>
    /// Structured reason the row was placed in mandatory review (US_041 EC-1).
    /// <see cref="ReviewReason.None"/> when confidence meets the auto-approve threshold.
    /// </summary>
    public ReviewReason ReviewReason { get; set; } = ReviewReason.None;

    // -------------------------------------------------------------------------
    // Archive support (US_042 task_004 AC-3, EC-1)
    // -------------------------------------------------------------------------

    /// <summary>
    /// True when this row has been superseded by a replacement document's extraction.
    /// Archived rows are retained for audit and rollback but excluded from active review
    /// workflows by default (AC-3, EC-1).
    /// </summary>
    public bool IsArchived { get; set; } = false;

    /// <summary>
    /// UTC timestamp when this row was archived (AC-3).
    /// Null while the row is still active.
    /// </summary>
    public DateTime? ArchivedAtUtc { get; set; }

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    public ClinicalDocument Document { get; set; } = null!;

    public ApplicationUser? VerifiedByUser { get; set; }
}
