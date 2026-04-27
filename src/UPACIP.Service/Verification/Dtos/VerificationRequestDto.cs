namespace UPACIP.Service.Verification.Dtos;

/// <summary>
/// Input DTO for a single or batch verification request (US_075 task_001).
/// Used by <c>IVerificationEnforcementService</c> for approve, modify, and reject operations.
/// </summary>
public sealed class VerificationRequestDto
{
    /// <summary>
    /// Primary key of the record being verified.
    /// For <c>recordType = "MedicalCode"</c> this is a <c>MedicalCode.Id</c>;
    /// for <c>recordType = "ExtractedData"</c> this is an <c>ExtractedData.Id</c>.
    /// </summary>
    public Guid RecordId { get; init; }

    /// <summary>
    /// Discriminator identifying the target entity type.
    /// Accepted values: <c>"MedicalCode"</c>, <c>"ExtractedData"</c>.
    /// </summary>
    public string RecordType { get; init; } = string.Empty;

    /// <summary>
    /// ID of the staff member performing the verification.
    /// Captured from the authenticated user context by the calling controller.
    /// </summary>
    public Guid StaffUserId { get; init; }

    /// <summary>
    /// New value provided by staff when modifying an AI-generated code or data value.
    /// <c>null</c> for plain approvals and rejections.
    /// Max 500 characters.
    /// </summary>
    public string? NewValue { get; init; }

    /// <summary>
    /// Staff-provided justification for a modification or rejection.
    /// Required when <see cref="NewValue"/> is populated or for rejections.
    /// Max 1000 characters.
    /// </summary>
    public string? Justification { get; init; }
}
