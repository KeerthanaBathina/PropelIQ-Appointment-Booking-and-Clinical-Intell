namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Lifecycle states for a patient account (US_012, AC-1, AC-2).
///
/// State transitions:
///   PendingVerification → Active (email confirmed, AC-2)
///   Active → Suspended (administrative action)
///   Active → Deactivated (patient self-deactivation or admin)
///   Suspended → Active (admin reactivation)
/// </summary>
public enum AccountStatus
{
    /// <summary>Account created but email not yet confirmed (AC-1).</summary>
    PendingVerification = 0,

    /// <summary>Email confirmed; account fully operational (AC-2).</summary>
    Active = 1,

    /// <summary>Temporarily suspended by an administrator.</summary>
    Suspended = 2,

    /// <summary>Permanently deactivated (soft-deleted; row preserved for audit trail).</summary>
    Deactivated = 3,
}
