namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Classifies the nature of a payer-specific code validation rule (US_051, AC-1, AC-2).
/// </summary>
public enum PayerRuleType
{
    /// <summary>Two codes cannot be billed together for the same encounter (e.g. mutually exclusive procedures).</summary>
    CombinationInvalid,

    /// <summary>A modifier must be appended to the code to support billing (e.g. modifier 25 or 59).</summary>
    ModifierRequired,

    /// <summary>Additional clinical documentation is required before the claim is accepted.</summary>
    DocumentationRequired,

    /// <summary>The code has a frequency limit (e.g. once per year) that has been exceeded.</summary>
    FrequencyLimit,
}
