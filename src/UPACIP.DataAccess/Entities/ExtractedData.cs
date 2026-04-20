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

    /// <summary>Model name and version string used to produce this extraction.</summary>
    public string SourceAttribution { get; set; } = string.Empty;

    /// <summary>True when the confidence score is below the auto-approve threshold.</summary>
    public bool FlaggedForReview { get; set; }

    /// <summary>FK to the <see cref="ApplicationUser"/> who verified this extraction. Null if unreviewed.</summary>
    public Guid? VerifiedByUserId { get; set; }

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    public ClinicalDocument Document { get; set; } = null!;

    public ApplicationUser? VerifiedByUser { get; set; }
}
