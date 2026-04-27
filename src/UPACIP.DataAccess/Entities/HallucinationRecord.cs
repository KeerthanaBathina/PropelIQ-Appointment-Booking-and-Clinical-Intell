using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Records the outcome of a staff member verifying an AI-generated medical justification
/// against its source clinical document (US_074 task_002, AC-1, AIR-Q06).
///
/// <para>
/// One record is created per verified AI justification.  When <see cref="SourceSupportStatus"/>
/// is <see cref="SourceSupportStatus.Unsupported"/>, the justification is classified as a
/// hallucination and counted in the daily hallucination rate calculation.
/// </para>
///
/// <para>
/// The <see cref="IsRetroactive"/> flag handles the edge case where staff discover
/// unsupported data after initial approval.  Retroactive records reset the
/// <c>MedicalCode.ApprovedByUserId</c> to null and trigger a
/// <see cref="HallucinationAlert"/> with <c>IsRetroactive = true</c>.
/// </para>
/// </summary>
public sealed class HallucinationRecord : BaseEntity
{
    /// <summary>
    /// FK to the <see cref="MedicalCode"/> whose AI-generated justification was verified.
    /// </summary>
    public Guid MedicalCodeId { get; set; }

    /// <summary>
    /// FK to the <see cref="ApplicationUser"/> (staff member) who performed the verification.
    /// </summary>
    public Guid VerifiedByUserId { get; set; }

    /// <summary>
    /// Classification of how well the AI justification is supported by the source document.
    /// <see cref="SourceSupportStatus.Unsupported"/> marks a hallucination.
    /// </summary>
    public SourceSupportStatus SourceSupportStatus { get; set; } = SourceSupportStatus.Pending;

    /// <summary>
    /// Optional staff-provided explanation for the classification decision.
    /// Recorded for audit purposes. Must not contain patient PII — reference IDs only.
    /// Max 1000 characters.
    /// </summary>
    public string? VerificationNotes { get; set; }

    /// <summary>UTC timestamp when this verification was submitted by staff.</summary>
    public DateTime VerifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// <c>true</c> when the hallucination was discovered after the original justification
    /// was already approved by staff (edge case per US_074).  Triggers a retroactive
    /// <see cref="HallucinationAlert"/> and resets the <c>MedicalCode.ApprovedByUserId</c>.
    /// </summary>
    public bool IsRetroactive { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The medical code whose justification was verified.</summary>
    public MedicalCode MedicalCode { get; set; } = null!;
}
