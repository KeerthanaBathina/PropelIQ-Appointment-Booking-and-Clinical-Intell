using UPACIP.DataAccess.Entities;

namespace UPACIP.Service.Coding;

/// <summary>
/// Contract for the CPT procedure code review workflow (US_048, AC-1, AC-3):
/// reading AI-suggested codes pending staff review, approving correct suggestions,
/// and overriding incorrect ones with clinically accurate replacements.
///
/// Unlike <see cref="IIcd10CodingService"/>, this interface does not include a
/// <c>Generate</c> method.  The AI procedure code generation pipeline is added in
/// <c>task_004_ai_cpt_prompt_rag</c> once the RAG retrieval layer is in place.
///
/// OWASP A01 note: all write methods receive the acting user's ID from the controller
/// (extracted from the JWT bearer token) — never from the request body.
/// </summary>
public interface ICptCodingService
{
    /// <summary>
    /// Returns all pending (unapproved) CPT <c>MedicalCode</c> entries for the given patient,
    /// sorted by relevance rank ascending then created-at descending.
    /// Results are cached for 5 minutes (NFR-030).
    /// </summary>
    /// <param name="patientId">Target patient primary key.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<MedicalCode>> GetPendingCodesAsync(
        Guid              patientId,
        CancellationToken ct = default);

    /// <summary>
    /// Marks a CPT <c>MedicalCode</c> row as approved by the acting staff member (US_048, AC-1).
    ///
    /// Sets <c>approved_by_user_id</c> to <paramref name="approvedByUserId"/> and writes an
    /// <c>AuditLog</c> entry with action <c>CptCodeApproved</c> (HIPAA §164.312(b), NFR-035).
    /// Invalidates the patient's pending-codes cache.
    /// </summary>
    /// <param name="medicalCodeId">PK of the <c>MedicalCode</c> row to approve.</param>
    /// <param name="approvedByUserId">ID of the staff member performing the approval (from JWT).</param>
    /// <param name="correlationId">Request trace correlation ID for structured logging.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated <c>MedicalCode</c> row.</returns>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when <paramref name="medicalCodeId"/> is not found or does not belong to a CPT code.
    /// </exception>
    Task<MedicalCode> ApproveCptCodeAsync(
        Guid              medicalCodeId,
        Guid              approvedByUserId,
        string            correlationId,
        CancellationToken ct = default);

    /// <summary>
    /// Replaces an AI-suggested CPT code with a clinically accurate alternative (US_048, edge case).
    ///
    /// Updates <c>code_value</c> to <paramref name="replacementCode"/>, stores the
    /// <paramref name="justification"/> text in <c>MedicalCode.Justification</c> for HIPAA
    /// auditability, sets <c>approved_by_user_id</c> to <paramref name="overriddenByUserId"/>,
    /// and writes an <c>AuditLog</c> entry with action <c>CptCodeOverridden</c>.
    /// Invalidates the patient's pending-codes cache.
    /// </summary>
    /// <param name="medicalCodeId">PK of the <c>MedicalCode</c> row to override.</param>
    /// <param name="replacementCode">Correct CPT code value (max 10 chars, AMA CPT format).</param>
    /// <param name="justification">Clinical reason for the override (min 10 chars, max 1 000).</param>
    /// <param name="overriddenByUserId">ID of the staff member performing the override (from JWT).</param>
    /// <param name="correlationId">Request trace correlation ID for structured logging.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The updated <c>MedicalCode</c> row.</returns>
    /// <exception cref="KeyNotFoundException">
    /// Thrown when <paramref name="medicalCodeId"/> is not found or does not belong to a CPT code.
    /// </exception>
    Task<MedicalCode> OverrideCptCodeAsync(
        Guid              medicalCodeId,
        string            replacementCode,
        string            justification,
        Guid              overriddenByUserId,
        string            correlationId,
        CancellationToken ct = default);
}
