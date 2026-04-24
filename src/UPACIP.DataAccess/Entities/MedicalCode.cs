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
    // ICD-10 library validation extensions (US_047 AC-3, AC-4, EC-1)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Rank assigned by the AI pipeline when multiple ICD-10 codes apply to a single
    /// diagnosis (US_047 AC-4).  A value of <c>1</c> indicates the highest-relevance code.
    /// <c>null</c> for CPT codes, manually entered codes, or when only a single code applies.
    /// </summary>
    public int? RelevanceRank { get; set; }

    /// <summary>
    /// Revalidation lifecycle state tracked after quarterly ICD-10 library refresh cycles
    /// (US_047 AC-3).  <c>null</c> until the first validation pass or for CPT codes.
    /// When set to <see cref="RevalidationStatus.DeprecatedReplaced"/>, staff must review
    /// and apply the replacement code from the ICD-10 library (edge case).
    /// </summary>
    public RevalidationStatus? RevalidationStatus { get; set; }

    /// <summary>
    /// The <c>LibraryVersion</c> of the <c>Icd10CodeLibrary</c> entry against which this
    /// code was last validated (e.g. <c>"2026.1"</c>).  <c>null</c> for CPT codes or
    /// codes entered before library versioning was introduced.
    /// Max 20 characters — matches the bound on <c>Icd10CodeLibrary.LibraryVersion</c>.
    /// </summary>
    public string? LibraryVersion { get; set; }

    // -------------------------------------------------------------------------
    // CPT-specific fields (US_048 AC-3 — bundled procedure support)
    // -------------------------------------------------------------------------

    /// <summary>
    /// When the AI identifies that multiple CPT codes form a billable bundle (US_048 AC-3, edge case),
    /// this flag is set to <c>true</c> for the bundled composite code row.  Individual component code
    /// rows in the same bundle share the same <see cref="BundleGroupId"/>.
    /// <c>false</c> for ICD-10 codes and non-bundled CPT codes.
    /// </summary>
    public bool IsBundled { get; set; } = false;

    /// <summary>
    /// Groups CPT code rows that belong to the same bundled procedure set (US_048 AC-3, edge case).
    /// All rows with the same non-null value form a bundle: one composite + one or more components.
    /// <c>null</c> for ICD-10 codes and non-bundled CPT codes.
    /// </summary>
    public Guid? BundleGroupId { get; set; }

    // -------------------------------------------------------------------------
    // Verification lifecycle fields (US_049 AC-2, AC-4, EC-1)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Staff-review lifecycle state for this code record (US_049 AC-2).
    /// Defaults to <see cref="CodeVerificationStatus.Pending"/> so that all AI-generated
    /// codes require an explicit staff decision before being considered final.
    /// </summary>
    public CodeVerificationStatus VerificationStatus { get; set; } = CodeVerificationStatus.Pending;

    /// <summary>
    /// FK to the <see cref="ApplicationUser"/> who performed the most recent verification or
    /// override action.  <c>null</c> until a staff member reviews the code (US_049 AC-2).
    /// </summary>
    public Guid? VerifiedByUserId { get; set; }

    /// <summary>
    /// UTC timestamp of the most recent verification or override action.
    /// <c>null</c> until a staff member reviews the code (US_049 AC-2).
    /// </summary>
    public DateTime? VerifiedAt { get; set; }

    /// <summary>
    /// Free-text reason provided by staff when overriding the AI-suggested code with a
    /// different value (US_049 AC-4).  <c>null</c> for non-overridden codes.
    /// Max 1 000 characters.
    /// </summary>
    public string? OverrideJustification { get; set; }

    /// <summary>
    /// The original AI-suggested <see cref="CodeValue"/> captured at the time of override
    /// so that the audit trail records what was changed (US_049 AC-4).
    /// <c>null</c> for non-overridden codes.  Max 20 characters.
    /// </summary>
    public string? OriginalCodeValue { get; set; }

    /// <summary>
    /// <c>true</c> when the code library (ICD-10 or CPT) marks this code as deprecated after
    /// a quarterly refresh.  When set, the system blocks re-approval and shows a deprecation
    /// notice to staff (US_049 EC-1, AC-4).
    /// </summary>
    public bool IsDeprecated { get; set; } = false;

    // -------------------------------------------------------------------------
    // Payer rule validation fields (US_051 AC-1, AC-4, task_003_db_payer_rules_schema)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Result of the most recent payer rule validation pass for this code (US_051 AC-1).
    /// Defaults to <see cref="PayerValidationStatus.NotValidated"/> until the validation
    /// service runs after code suggestion or staff assignment.
    /// </summary>
    public PayerValidationStatus PayerValidationStatus { get; set; } = PayerValidationStatus.NotValidated;

    /// <summary>
    /// Result of the most recent NCCI bundling rule check for the code set this code
    /// belongs to (US_051 AC-4).  Set on the entire patient code set when bundling
    /// validation is triggered.
    /// </summary>
    public BundlingCheckResult BundlingCheckResult { get; set; } = BundlingCheckResult.NotChecked;

    /// <summary>
    /// Billing priority ordering for multi-code assignment (US_051 AC-3).
    /// Lower values are billed first; 0 is the default for single-code scenarios.
    /// </summary>
    public int SequenceOrder { get; set; } = 0;

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    public Patient Patient { get; set; } = null!;

    public ApplicationUser? ApprovedByUser { get; set; }

    public ApplicationUser? VerifiedByUser { get; set; }

    public ICollection<CodingAuditLog> CodingAuditLogs { get; set; } = [];
}
