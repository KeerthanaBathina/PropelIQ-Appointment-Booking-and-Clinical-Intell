using Microsoft.Extensions.Logging;

namespace UPACIP.Service.Auth;

/// <summary>
/// Manages the single-active-session policy and session lifecycle operations needed
/// during authentication (AC-2, FR-007, NFR-015, US_065 AC-2).
///
/// Behavior change from US_014:
///   US_014 — rejected new login attempts with 409 when a session was already active.
///   US_065 — terminates the existing session and ALLOWS the new login (latest session wins).
///
/// The old device receives a 440 SESSION_TERMINATED response on its next authenticated
/// request via <see cref="ISessionService.CheckAndClearTerminationFlagAsync"/>.
/// </summary>
public sealed class ConcurrentSessionGuard
{
    private readonly ISessionService _sessionService;
    private readonly ILogger<ConcurrentSessionGuard> _logger;

    public ConcurrentSessionGuard(
        ISessionService sessionService,
        ILogger<ConcurrentSessionGuard> logger)
    {
        _sessionService = sessionService;
        _logger         = logger;
    }

    /// <summary>
    /// Handles concurrent session enforcement for a new login attempt (US_065 AC-2).
    ///
    /// If an active session exists for <paramref name="userId"/>, it is terminated and a
    /// one-time termination flag is stored in Redis so the old device is notified on its
    /// next request. The new login is ALWAYS allowed (latest session wins).
    ///
    /// Returns a <see cref="SessionTerminationResult"/> describing the terminated session
    /// (callers should blacklist the old JWT and write an audit log entry).
    /// Returns a result with <c>WasTerminated = false</c> when no existing session was active.
    /// </summary>
    public async Task<SessionTerminationResult> HandleConcurrentSessionAsync(
        string userId,
        string attemptIpAddress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _sessionService.TerminateAndReplaceSessionAsync(userId, cancellationToken);
        }
        catch (Exception ex)
        {
            // Redis unavailable — fail open so authentication is not blocked by cache outage.
            _logger.LogWarning(
                ex,
                "ConcurrentSessionGuard could not query Redis for user {UserId}. Allowing login (fail-open).",
                userId);
            return new SessionTerminationResult { WasTerminated = false };
        }
    }

    /// <summary>
    /// Creates a Redis session entry after a successful login (delegates to <see cref="ISessionService.CreateSessionAsync"/>).
    /// </summary>
    public Task CreateAsync(
        string userId,
        string sessionId,
        string jti,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default)
        => _sessionService.CreateSessionAsync(userId, sessionId, jti, ipAddress, userAgent, cancellationToken);

    /// <summary>
    /// Deletes the Redis session on explicit logout (delegates to <see cref="ISessionService.InvalidateSessionAsync"/>).
    /// </summary>
    public Task InvalidateAsync(string userId, CancellationToken cancellationToken = default)
        => _sessionService.InvalidateSessionAsync(userId, cancellationToken);
}

/// <summary>Outcome of a concurrent-session gate check (retained for backward compatibility).</summary>
public enum ConcurrentSessionResult
{
    /// <summary>No active session found — login may proceed.</summary>
    Allowed,

    /// <summary>Active session exists — login must be rejected with 409 Conflict (US_014 behavior, superseded by US_065).</summary>
    [Obsolete("US_065 changed behavior: concurrent sessions are now terminated, not rejected. Use HandleConcurrentSessionAsync instead.")]
    Blocked,
}
