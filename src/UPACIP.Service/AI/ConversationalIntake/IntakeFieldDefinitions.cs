namespace UPACIP.Service.AI.ConversationalIntake;

/// <summary>
/// Defines the mandatory and optional intake fields per FR-029 and FR-030.
///
/// Mandatory fields (AC-3, FR-029): must all be collected before intake is complete.
/// Optional fields (FR-030): offered after mandatory collection; patient may skip.
/// </summary>
public static class IntakeFieldDefinitions
{
    // ── Mandatory fields (FR-029) ─────────────────────────────────────────────

    public const string FullName                      = "full_name";
    public const string DateOfBirth                   = "date_of_birth";
    public const string ContactPhone                  = "contact_phone";
    public const string ContactEmail                  = "contact_email";
    public const string EmergencyContactName          = "emergency_contact_name";
    public const string EmergencyContactPhone         = "emergency_contact_phone";
    public const string EmergencyContactRelationship  = "emergency_contact_relationship";

    // ── Optional fields (FR-030) ──────────────────────────────────────────────

    public const string InsuranceProvider      = "insurance_provider";
    public const string InsurancePolicyNumber  = "insurance_policy_number";
    public const string MedicalHistory         = "medical_history";
    public const string CurrentMedications     = "current_medications";
    public const string KnownAllergies         = "known_allergies";

    // ── Ordered collection sequences ─────────────────────────────────────────

    /// <summary>Ordered sequence of mandatory fields (FR-029, AC-3).</summary>
    public static readonly IReadOnlyList<string> MandatoryOrder =
    [
        FullName,
        DateOfBirth,
        ContactPhone,
        ContactEmail,
        EmergencyContactName,
        EmergencyContactPhone,
        EmergencyContactRelationship,
    ];

    /// <summary>Ordered sequence of optional fields (FR-030).</summary>
    public static readonly IReadOnlyList<string> OptionalOrder =
    [
        InsuranceProvider,
        InsurancePolicyNumber,
        MedicalHistory,
        CurrentMedications,
        KnownAllergies,
    ];

    /// <summary>All field keys in collection order (mandatory first, then optional).</summary>
    public static readonly IReadOnlyList<string> AllFields =
        [.. MandatoryOrder, .. OptionalOrder];

    /// <summary>Human-readable label for each field key.</summary>
    public static readonly IReadOnlyDictionary<string, string> Labels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FullName]                     = "Full Name",
            [DateOfBirth]                  = "Date of Birth",
            [ContactPhone]                 = "Phone Number",
            [ContactEmail]                 = "Email Address",
            [EmergencyContactName]         = "Emergency Contact Name",
            [EmergencyContactPhone]        = "Emergency Contact Phone",
            [EmergencyContactRelationship] = "Emergency Contact Relationship",
            [InsuranceProvider]            = "Insurance Provider",
            [InsurancePolicyNumber]        = "Insurance Policy Number",
            [MedicalHistory]               = "Medical History",
            [CurrentMedications]           = "Current Medications",
            [KnownAllergies]               = "Known Allergies",
        };

    /// <summary>Returns true when all mandatory fields are present in <paramref name="collected"/>.</summary>
    public static bool AreMandatoryFieldsComplete(IReadOnlyDictionary<string, string> collected)
    {
        foreach (var key in MandatoryOrder)
        {
            if (!collected.ContainsKey(key) || string.IsNullOrWhiteSpace(collected[key]))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Returns the next field key that has not yet been collected, starting with mandatory fields.
    /// Returns null when all fields have been collected.
    /// </summary>
    public static string? NextFieldToCollect(IReadOnlyDictionary<string, string> collected)
    {
        foreach (var key in AllFields)
        {
            if (!collected.ContainsKey(key) || string.IsNullOrWhiteSpace(collected[key]))
                return key;
        }
        return null;
    }

    public static bool IsMandatory(string fieldKey) => MandatoryOrder.Contains(fieldKey);
}
