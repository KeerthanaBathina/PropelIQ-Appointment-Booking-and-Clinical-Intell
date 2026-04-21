namespace UPACIP.DataAccess.Entities.OwnedTypes;

/// <summary>
/// JSONB owned type stored in the <c>guardian_consent</c> column of <c>intake_data</c>.
///
/// Captures the guardian identity details and consent acknowledgment required when a
/// patient's DOB indicates they are a minor (under 18) (US_031, AC-1, EC-1).
///
/// Design notes:
///   - Nullable column: NULL means guardian consent was not required for this intake record
///     (i.e., patient is an adult or intake was completed before this feature was deployed).
///   - GuardianDateOfBirth is stored as an ISO-8601 date string (YYYY-MM-DD) for JSONB
///     compatibility; the service layer parses it with DateOnly.TryParse when age-verifying.
///   - ConsentRecordedAt is stored as UTC so audit queries are timezone-independent.
///   - EC-1: To verify guardian age >= 18, consumers must parse GuardianDateOfBirth as a
///     DateOnly and compute the age relative to ConsentRecordedAt or DateOnly.FromDateTime(UtcNow).
/// </summary>
public sealed class GuardianConsentSnapshot
{
    /// <summary>Full legal name of the consenting guardian.</summary>
    public string? GuardianName { get; set; }

    /// <summary>
    /// Date of birth of the consenting guardian in ISO-8601 format (YYYY-MM-DD).
    /// Required to verify guardian is >= 18 years old (EC-1).
    /// </summary>
    public string? GuardianDateOfBirth { get; set; }

    /// <summary>
    /// Relationship of the guardian to the minor patient
    /// (e.g., "Parent", "Legal Guardian", "Grandparent").
    /// </summary>
    public string? GuardianRelationship { get; set; }

    /// <summary>
    /// Whether the guardian explicitly acknowledged and accepted the consent terms
    /// in the intake form UI.  Must be <c>true</c> for a minor booking to proceed.
    /// </summary>
    public bool ConsentAcknowledged { get; set; }

    /// <summary>UTC timestamp when the guardian consent was recorded and persisted.</summary>
    public DateTime? ConsentRecordedAt { get; set; }
}
