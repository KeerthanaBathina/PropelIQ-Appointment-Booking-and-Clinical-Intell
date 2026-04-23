namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Tracks the staff-review lifecycle state of a <c>MedicalCode</c> record (US_049, AC-2, AC-4).
/// </summary>
public enum CodeVerificationStatus
{
    /// <summary>
    /// Code has been AI-suggested but not yet reviewed by staff.
    /// This is the default state for all AI-generated codes (AC-2).
    /// </summary>
    Pending,

    /// <summary>
    /// A staff member has reviewed and confirmed the AI-suggested code is correct
    /// (<c>verified_by_user_id</c> and <c>verified_at</c> are populated, AC-2).
    /// </summary>
    Verified,

    /// <summary>
    /// A staff member replaced the AI-suggested code with a different value
    /// (<c>override_justification</c> and <c>original_code_value</c> are populated, AC-4).
    /// </summary>
    Overridden,

    /// <summary>
    /// The code exists in the database but has been superseded by a library update.
    /// Staff must re-evaluate and either re-verify with the replacement code or override (EC-1).
    /// </summary>
    Deprecated,
}
