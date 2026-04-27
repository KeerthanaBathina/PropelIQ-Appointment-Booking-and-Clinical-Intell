using UPACIP.Service.Verification.Dtos;

namespace UPACIP.Service.Verification;

/// <summary>
/// Backend enforcement layer ensuring no AI-generated medical output enters production
/// records without human verification per AIR-S03 and US_075.
///
/// <para>Responsibilities:</para>
/// <list type="bullet">
///   <item>Approve AI outputs — sets status to <c>Verified</c> with staff attribution
///   and creates an immutable audit entry (AC-1, AC-3).</item>
///   <item>Modify and approve — stores the original AI value, applies the staff correction,
///   and logs both old and new values (AC-3).</item>
///   <item>Reject AI outputs — marks the record as <c>Rejected</c> so it is excluded from
///   downstream workflows (AC-1, AC-3).</item>
///   <item>Batch approve up to 50+ items with one call — creates individual audit entries
///   per record (edge case).</item>
///   <item>Check verification status — used by the enforcement filter to block finalization
///   of unverified records (AC-4).</item>
/// </list>
///
/// <para>
/// <b>By design, no auto-approval mechanism exists for clinical data.</b>  Records in
/// <c>PendingVerification</c>/<c>Pending</c> status remain there indefinitely until a
/// staff member acts — per AIR-S03 compliance (edge case: no staff available).
/// </para>
///
/// <para>Supports two record types: <c>"MedicalCode"</c> and <c>"ExtractedData"</c>.
/// Uses <c>CodingAuditLog</c> for medical code operations and <c>AuditLog</c> for
/// extracted data operations.</para>
///
/// <para>Registered as Scoped — shares the DI scope with <c>ApplicationDbContext</c>.</para>
/// </summary>
public interface IVerificationEnforcementService
{
    /// <summary>
    /// Approves a single AI-generated record without modification (AC-1, AC-3).
    ///
    /// <para>Sets <c>VerificationStatus = Verified</c>, stamps <c>VerifiedAt</c> and
    /// <c>VerifiedByUserId</c>, and creates an immutable audit entry logging the staff ID,
    /// timestamp, and original AI value.</para>
    /// </summary>
    /// <param name="recordId">PK of the target record.</param>
    /// <param name="recordType"><c>"MedicalCode"</c> or <c>"ExtractedData"</c>.</param>
    /// <param name="staffUserId">ID of the staff member approving the record.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Audit entry DTO, or <c>null</c> when the record was not found.</returns>
    Task<VerificationAuditEntryDto?> ApproveAsync(
        Guid              recordId,
        string            recordType,
        Guid              staffUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Approves a record after applying a staff correction (AC-1, AC-3).
    ///
    /// <para>Stores the original AI value in the record's audit fields, applies
    /// <paramref name="newValue"/>, sets <c>VerificationStatus = Modified/Corrected</c>,
    /// and creates an audit entry with both old and new values plus the justification.</para>
    /// </summary>
    /// <param name="recordId">PK of the target record.</param>
    /// <param name="recordType"><c>"MedicalCode"</c> or <c>"ExtractedData"</c>.</param>
    /// <param name="staffUserId">ID of the staff member performing the modification.</param>
    /// <param name="newValue">Staff-corrected value to apply.</param>
    /// <param name="justification">Reason for the correction (required).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Audit entry DTO, or <c>null</c> when the record was not found.</returns>
    Task<VerificationAuditEntryDto?> ModifyAndApproveAsync(
        Guid              recordId,
        string            recordType,
        Guid              staffUserId,
        string            newValue,
        string            justification,
        CancellationToken ct = default);

    /// <summary>
    /// Rejects a single AI-generated record (AC-1, AC-3).
    ///
    /// <para>Sets <c>VerificationStatus = Rejected</c>, stamps <c>VerifiedAt</c> and
    /// <c>VerifiedByUserId</c>, and creates an audit entry with the rejection reason.</para>
    /// </summary>
    /// <param name="recordId">PK of the target record.</param>
    /// <param name="recordType"><c>"MedicalCode"</c> or <c>"ExtractedData"</c>.</param>
    /// <param name="staffUserId">ID of the staff member rejecting the record.</param>
    /// <param name="reason">Staff-provided rejection reason.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Audit entry DTO, or <c>null</c> when the record was not found.</returns>
    Task<VerificationAuditEntryDto?> RejectAsync(
        Guid              recordId,
        string            recordType,
        Guid              staffUserId,
        string            reason,
        CancellationToken ct = default);

    /// <summary>
    /// Approves a batch of AI-generated records in a single call (edge case: 50+ items).
    ///
    /// <para>Creates individual audit entries per record — not a single bulk entry —
    /// preserving the per-item audit trail required for HIPAA compliance (AC-3).</para>
    ///
    /// <para>Records already in <c>Verified</c>, <c>Modified</c>, or <c>Rejected</c> state
    /// are silently skipped (idempotent).</para>
    /// </summary>
    /// <param name="recordIds">List of record PKs to approve.</param>
    /// <param name="recordType"><c>"MedicalCode"</c> or <c>"ExtractedData"</c>.</param>
    /// <param name="staffUserId">ID of the staff member performing the batch approval.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of audit entry DTOs for each successfully approved record.</returns>
    Task<List<VerificationAuditEntryDto>> BatchApproveAsync(
        IReadOnlyList<Guid> recordIds,
        string              recordType,
        Guid                staffUserId,
        CancellationToken   ct = default);

    /// <summary>
    /// Returns <c>true</c> when the specified record has been verified (status is
    /// <c>Verified</c>, <c>Modified/Corrected</c>, or <c>Rejected</c>).
    ///
    /// <para>Used by <c>VerificationRequiredFilter</c> to block finalization of unverified
    /// records at the API layer (AC-4).</para>
    /// </summary>
    /// <param name="recordId">PK of the target record.</param>
    /// <param name="recordType"><c>"MedicalCode"</c> or <c>"ExtractedData"</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<bool> IsVerifiedAsync(
        Guid              recordId,
        string            recordType,
        CancellationToken ct = default);
}
