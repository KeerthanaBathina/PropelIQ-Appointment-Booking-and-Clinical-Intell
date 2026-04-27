namespace UPACIP.Service.Auth;

/// <summary>
/// Abstraction for Redis-backed session lifecycle management (NFR-014, NFR-015, FR-003, FR-007).
///
/// Key structure: <c>session:{userId}</c> → <see cref="SessionData"/> JSON, 15-minute sliding TTL.
/// All methods degrade gracefully when Redis is unavailable — callers must not propagate cache
/// failures to the user (circuit breaker pattern per NFR-023).
/// </summary>
public interface ISessionService
{
    /// <summary>
    /// Creates a new session entry in Redis for <paramref name="userId"/> with a 15-minute
    /// sliding TTL. An existing entry is overwritten (used when re-creating after forced logout).
    /// </summary>
    Task CreateSessionAsync(
        string userId,
        string sessionId,
        string jti,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the session TTL to 15 minutes from now, recording the current UTC time as
    /// <see cref="SessionData.LastActivity"/>. This is the AC-2 sliding-window reset.
    /// No-op when no active session exists (key has already expired).
    /// </summary>
    Task UpdateActivityAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current session data for <paramref name="userId"/>, or <c>null</c> when
    /// the session has expired or was never created.
    /// </summary>
    Task<SessionData?> GetSessionAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the session key from Redis immediately (explicit logout / forced invalidation).
    /// </summary>
    Task InvalidateSessionAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <c>true</c> when an active (non-expired) session exists for <paramref name="userId"/>
    /// in Redis. Used by <c>ConcurrentSessionGuard</c> to reject second-device logins (AC-3, FR-007).
    /// </summary>
    Task<bool> IsSessionActiveAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Terminates the existing session for <paramref name="userId"/> (if any) and stores a
    /// short-lived (<c>session_terminated:{oldJti}</c>) notification flag in Redis so that the
    /// old device receives a 440 response on its next authenticated request (US_065 AC-2).
    ///
    /// Returns a <see cref="SessionTerminationResult"/> describing the terminated session.
    /// When no active session exists, <see cref="SessionTerminationResult.WasTerminated"/> is
    /// <c>false</c> and all other fields are null.
    ///
    /// Does NOT create the replacement session — call <see cref="CreateSessionAsync"/> afterward.
    /// </summary>
    Task<SessionTerminationResult> TerminateAndReplaceSessionAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the remaining session TTL in seconds for <paramref name="userId"/>, or
    /// <c>null</c> when no active session key exists.
    /// Used by <c>GET /api/session/time-remaining</c> to drive the frontend countdown modal (AC-4).
    /// </summary>
    Task<int?> GetTimeRemainingAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically reads and deletes the <c>session_terminated:{jti}</c> flag from Redis.
    /// Returns the termination-reason JSON payload when the flag exists, <c>null</c> otherwise.
    /// The one-time read ensures Device A only receives the 440 SESSION_TERMINATED response once
    /// (subsequent requests after re-authentication are not affected).
    /// </summary>
    Task<string?> CheckAndClearTerminationFlagAsync(string jti, CancellationToken cancellationToken = default);
}
