namespace UPACIP.Service.Verification.Dtos;

/// <summary>
/// Audit entry DTO returned by each verification operation, carrying the original AI value,
/// the final accepted value, staff identity, timestamp, and action type (US_075, AC-3).
///
/// <para>
/// This DTO is returned to the caller for immediate feedback and can be included in API
/// responses.  It mirrors the record stored in <c>CodingAuditLog</c> (for
/// <c>MedicalCode</c> operations) or <c>AuditLog</c> (for <c>ExtractedData</c> operations).
/// </para>
///
/// <para>No patient PII is included — only record IDs and code/data values (AIR-S02, AIR-S03).</para>
/// </summary>
public sealed class VerificationAuditEntryDto
{
    /// <summary>ID of the record that was verified.</summary>
    public Guid RecordId { get; init; }

    /// <summary>
    /// Entity type of the verified record.
    /// One of <c>"MedicalCode"</c> or <c>"ExtractedData"</c>.
    /// </summary>
    public string RecordType { get; init; } = string.Empty;

    /// <summary>ID of the staff member who performed the verification.</summary>
    public Guid StaffUserId { get; init; }

    /// <summary>UTC timestamp when the verification action was recorded.</summary>
    public DateTime VerifiedAt { get; init; }

    /// <summary>
    /// The verification action taken: <c>"Approved"</c>, <c>"Modified"</c>, or <c>"Rejected"</c>.
    /// </summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>
    /// Original AI-generated value before the verification action.
    /// For <c>MedicalCode</c>: the original code value (e.g. <c>"J18.9"</c>).
    /// For <c>ExtractedData</c>: a description of the original extracted content.
    /// </summary>
    public string OriginalAiValue { get; init; } = string.Empty;

    /// <summary>
    /// Final accepted value after the verification action.
    /// Equal to <see cref="OriginalAiValue"/> for plain approvals.
    /// Set to the staff-provided replacement for modifications.
    /// Empty for rejections.
    /// </summary>
    public string FinalValue { get; init; } = string.Empty;

    /// <summary>
    /// Staff-provided justification for modifications or rejections.
    /// <c>null</c> for plain approvals.
    /// </summary>
    public string? Justification { get; init; }
}
