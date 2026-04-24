namespace UPACIP.Service.Coding;

/// <summary>
/// Assigns multiple ICD-10 and CPT codes to a patient encounter with individual
/// code verification and billing priority ordering (US_051, AC-3).
/// </summary>
public interface IMultiCodeAssignmentService
{
    /// <summary>
    /// Assigns multiple codes to the patient encounter.  Each code is validated against
    /// the ICD-10/CPT library (DR-015) and persisted with user attribution (US_051 AC-3).
    /// Idempotent — existing rows are updated (NFR-034).
    /// </summary>
    Task<MultiCodeAssignmentRunResult> AssignMultipleCodesAsync(
        Guid patientId,
        IReadOnlyList<CodeAssignmentItem> codes,
        Guid actingUserId,
        CancellationToken ct = default);

    /// <summary>
    /// Verifies (approves) a single code within a multi-code assignment (US_051 AC-3).
    /// Throws <see cref="KeyNotFoundException"/> when the code is not found.
    /// </summary>
    Task VerifySingleCodeAsync(
        Guid patientId,
        Guid codeId,
        Guid actingUserId,
        CancellationToken ct = default);
}
