namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Discriminates the type of staff action recorded in <c>CodingAuditLog</c> (US_049, AC-2, AC-4, FR-066).
/// </summary>
public enum CodingAuditAction
{
    /// <summary>
    /// Staff approved the AI-suggested code without modification (US_049 AC-2).
    /// </summary>
    Approved,

    /// <summary>
    /// Staff replaced the AI-suggested code with a different value and provided a justification (US_049 AC-4).
    /// </summary>
    Overridden,

    /// <summary>
    /// Approval was blocked because the target code is marked deprecated in the reference library (US_049 EC-1).
    /// </summary>
    DeprecatedBlocked,

    /// <summary>
    /// A code was re-evaluated against the current library (e.g. after a quarterly refresh, US_049 AC-4).
    /// </summary>
    Revalidated,
}
