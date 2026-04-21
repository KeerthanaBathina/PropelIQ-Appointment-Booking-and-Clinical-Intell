namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Describes why a user session was terminated.
/// Stored as an integer column in <c>user_sessions</c> for compact storage
/// while preserving readability via EF Core enum conversion.
/// </summary>
public enum SessionExpirationReason
{
    /// <summary>The user explicitly called <c>POST /api/auth/logout</c>.</summary>
    ExplicitLogout = 0,

    /// <summary>The 15-minute inactivity TTL elapsed without an activity refresh (NFR-014).</summary>
    InactivityTimeout = 1,

    /// <summary>Session was replaced when the user re-authenticated (e.g. after forced logout).</summary>
    ConcurrentSessionReplacement = 2,

    /// <summary>An administrator forcibly terminated the session.</summary>
    AdminForceLogout = 3,
}
