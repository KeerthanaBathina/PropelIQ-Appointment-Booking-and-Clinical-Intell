using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Represents a clinical code (ICD-10 or CPT) associated with a patient record.
/// Codes may be suggested by the AI pipeline and must be approved by an authorised user
/// before they are considered final.
/// </summary>
public sealed class MedicalCode : BaseEntity
{
    /// <summary>FK to the <see cref="Patient"/> this code is assigned to.</summary>
    public Guid PatientId { get; set; }

    /// <summary>Coding standard — ICD-10 (diagnosis) or CPT (procedure).</summary>
    public CodeType CodeType { get; set; }

    /// <summary>The alphanumeric code value (e.g. "J18.9", "99213").</summary>
    public string CodeValue { get; set; } = string.Empty;

    /// <summary>Human-readable description of the code.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Clinical justification for assigning this code to the patient.</summary>
    public string Justification { get; set; } = string.Empty;

    /// <summary>True when the code was proposed by the AI pipeline rather than entered manually.</summary>
    public bool SuggestedByAi { get; set; }

    /// <summary>FK to the <see cref="ApplicationUser"/> who approved the code. Null if pending or manually entered.</summary>
    public Guid? ApprovedByUserId { get; set; }

    /// <summary>AI model confidence score in the range [0.0, 1.0]. Null for manually entered codes.</summary>
    public float? AiConfidenceScore { get; set; }

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    public Patient Patient { get; set; } = null!;

    public ApplicationUser? ApprovedByUser { get; set; }
}
