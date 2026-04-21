using Microsoft.Extensions.Logging;

namespace UPACIP.Service.Auth;

/// <summary>
/// Manages the single-active-session policy and session lifecycle operations needed
/// during authentication (AC-3, FR-007, NFR-015).
///
/// Consolidates three auth-layer session operations so that <c>AuthController</c> only
/// needs a single session-related dependency:
/// <list type="bullet">
///   <item><term>CheckAsync</term><description>Rejects a login when an active session already exists (409 Conflict).</description></item>
///   <item><term>CreateAsync</term><description>Registers a new session in Redis after successful login.</description></item>
///   <item><term>InvalidateAsync</term><description>Removes the session from Redis on logout.</description></item>
/// </list>
///
/// Design: the guard does NOT invalidate the existing session on a conflict. The user
/// must explicitly logout from their current device before logging in on a new one.
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
    /// Checks whether a concurrent active session exists for <paramref name="userId"/>.
    /// </summary>
    /// <returns>
    /// <see cref="ConcurrentSessionResult.Allowed"/> when no active session exists and login
    /// may proceed. <see cref="ConcurrentSessionResult.Blocked"/> when an active session is
    /// already present — the caller must return 409 Conflict.
    /// </returns>
    public async Task<ConcurrentSessionResult> CheckAsync(
        string userId,
        string attemptIpAddress,
        CancellationToken cancellationToken = default)
    {
        SessionData? existing = null;

        try
        {
            existing = await _sessionService.GetSessionAsync(userId, cancellationToken);
        }
        catch (Exception ex)
        {
            // Redis unavailable — fail open so authentication is not blocked by cache outage.
            _logger.LogWarning(
                ex,
                "ConcurrentSessionGuard could not query Redis for user {UserId}. Allowing login (fail-open).",
                userId);
            return ConcurrentSessionResult.Allowed;
        }

        if (existing is null)
            return ConcurrentSessionResult.Allowed;

        // Active session found — reject the new login attempt (AC-3, FR-007).
        _logger.LogWarning(
            "Concurrent login rejected for user {UserId}. " +
            "Existing session {SessionId} last active at {LastActivity} from {ExistingIp}. " +
            "New attempt from {AttemptIp}.",
            userId,
            existing.SessionId,
            existing.LastActivity,
            existing.IpAddress,
            attemptIpAddress);

        return ConcurrentSessionResult.Blocked;
    }

    /// <summary>
    /// Creates a Redis session entry after a successful login (delegates to <see cref="ISessionService.CreateSessionAsync"/>).
    /// </summary>
    public Task CreateAsync(
        string userId,
        string sessionId,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default)
        => _sessionService.CreateSessionAsync(userId, sessionId, ipAddress, userAgent, cancellationToken);

    /// <summary>
    /// Deletes the Redis session on explicit logout (delegates to <see cref="ISessionService.InvalidateSessionAsync"/>).
    /// </summary>
    public Task InvalidateAsync(string userId, CancellationToken cancellationToken = default)
        => _sessionService.InvalidateSessionAsync(userId, cancellationToken);
}

/// <summary>Outcome of a concurrent-session gate check.</summary>
public enum ConcurrentSessionResult
{
    /// <summary>No active session found — login may proceed.</summary>
    Allowed,

    /// <summary>Active session exists — login must be rejected with 409 Conflict.</summary>
    Blocked,
}
