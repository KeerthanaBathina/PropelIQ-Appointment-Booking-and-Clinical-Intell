namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Indicates the urgency level of a payer rule violation (US_051, AC-2).
/// Maps to MUI Alert severity values on the frontend.
/// </summary>
public enum PayerRuleSeverity
{
    /// <summary>Claim will likely be denied — staff must take corrective action before submission.</summary>
    Error,

    /// <summary>Elevated denial risk — review recommended before submission.</summary>
    Warning,

    /// <summary>Informational guidance — low denial risk but staff should be aware.</summary>
    Info,
}
