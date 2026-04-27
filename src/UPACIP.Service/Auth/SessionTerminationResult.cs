namespace UPACIP.Service.Auth;

/// <summary>
/// Result of <see cref="ISessionService.TerminateAndReplaceSessionAsync"/> — carries
/// enough context for the caller to blacklist the old JWT and write an audit log entry
/// (US_065 AC-2, NFR-015).
/// </summary>
public sealed class SessionTerminationResult
{
    /// <summary>Whether an existing session was actually terminated (false when none was active).</summary>
    public bool WasTerminated { get; init; }

    /// <summary>UUID of the session that was terminated. Null when <see cref="WasTerminated"/> is false.</summary>
    public string? OldSessionId { get; init; }

    /// <summary>
    /// JWT <c>jti</c> of the token bound to the terminated session.
    /// Blacklist this via <c>ITokenService.BlacklistJtiAsync</c> to ensure that existing
    /// Device A tokens are immediately revoked (defence-in-depth beyond session deletion).
    /// Null when <see cref="WasTerminated"/> is false.
    /// </summary>
    public string? OldJti { get; init; }

    /// <summary>IP address of the terminated session's originating client.</summary>
    public string? OldIpAddress { get; init; }

    /// <summary>User-Agent of the terminated session's originating client.</summary>
    public string? OldUserAgent { get; init; }
}
