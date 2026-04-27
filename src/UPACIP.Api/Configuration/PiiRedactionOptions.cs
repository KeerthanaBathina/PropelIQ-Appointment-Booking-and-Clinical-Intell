namespace UPACIP.Api.Configuration;

/// <summary>
/// Strongly-typed options for PII redaction in the Serilog logging pipeline.
/// Bound from the <c>"PiiRedaction"</c> section in <c>appsettings.json</c>
/// (US_066 AC-2, NFR-017, EC-2).
///
/// Config-driven field names satisfy EC-2: adding a new PII field to
/// <c>appsettings.json</c> triggers redaction without code changes.
/// </summary>
public sealed class PiiRedactionOptions
{
    public const string SectionName = "PiiRedaction";

    /// <summary>
    /// Property names (case-insensitive) whose values should be treated as PII
    /// and redacted before any log sink writes the event.
    /// </summary>
    public IReadOnlyList<string> PiiFieldNames { get; set; } =
    [
        "Email",
        "PhoneNumber",
        "Phone",
        "Ssn",
        "SocialSecurityNumber",
        "DateOfBirth",
        "Dob",
        "BirthDate",
        "FirstName",
        "LastName",
        "PatientName",
        "FullName",
        "Name",
    ];

    /// <summary>
    /// Regex patterns keyed by PII category used to auto-detect PII in unstructured
    /// string values when the field name alone is insufficient.
    /// Keys are case-insensitive and correspond to the redaction method to apply:
    /// <c>Email</c>, <c>Phone</c>, <c>Ssn</c>.
    /// </summary>
    public IReadOnlyDictionary<string, string> PiiPatterns { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Email"] = @"^[\w._%+\-]+@[\w.\-]+\.\w{2,}$",
            ["Phone"] = @"^\+?\d[\d\s()\-]{7,}$",
            ["Ssn"]   = @"^\d{3}-?\d{2}-?\d{4}$",
        };
}
