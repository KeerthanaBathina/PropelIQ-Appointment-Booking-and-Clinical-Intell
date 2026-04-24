namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Lifecycle state of a <see cref="UPACIP.DataAccess.Entities.PayerRuleViolation"/> record (US_051, AC-2, edge case).
/// </summary>
public enum ViolationResolutionStatus
{
    /// <summary>Violation detected but not yet reviewed by staff.</summary>
    Pending,

    /// <summary>Staff accepted the corrective action suggestion and applied it.</summary>
    Accepted,

    /// <summary>Staff overrode the payer rule with clinical justification.</summary>
    Overridden,

    /// <summary>Staff dismissed the violation as not applicable for this encounter.</summary>
    Dismissed,
}
