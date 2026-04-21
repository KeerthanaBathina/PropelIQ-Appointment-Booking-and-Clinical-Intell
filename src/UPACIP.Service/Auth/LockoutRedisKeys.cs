namespace UPACIP.Service.Auth;

/// <summary>
/// Redis key pattern constants for account lockout rate-limiting and MFA verification
/// (US_016 TASK_003 — defined per Redis Key Structure spec).
///
/// Key lifecycle:
///
/// | Key Pattern                        | Type    | TTL        | Description                                                |
/// |------------------------------------|---------|------------|------------------------------------------------------------|
/// | lockout:attempts:{userId}          | STRING  | 30 minutes | Atomic failed-attempt counter (INCR). Deleted on success.  |
/// | lockout:until:{userId}             | STRING  | 30 minutes | ISO lockout-expiry timestamp for fast lookup (no DB query). |
/// | mfa:rate:{mfaToken_jti}            | STRING  | 5 minutes  | MFA verify attempt counter (max 5 per token).              |
///
/// Usage notes:
///   - <c>lockout:attempts:{userId}</c>: Use Redis INCR (atomic). Set EXPIRE 1800 on first increment
///     (NX). On successful login, DEL the key. Falls back to Identity's AccessFailedCount if Redis
///     is unavailable (fail-open pattern matching Identity lock enforcement).
///   - <c>lockout:until:{userId}</c>: Set when lockout triggers (SETEX 1800). Used for fast
///     lockout-check before DB query. Auto-expires; not authoritative — DB LockoutEnd is truth.
///   - <c>mfa:rate:{mfaToken_jti}</c>: Increment per MFA verify attempt. Rate limiter at the
///     controller layer uses the [EnableRateLimiting("mfa-verify-limit")] attribute (5 req/min per
///     IP) as the primary guard; this Redis key provides per-token secondary enforcement.
///
/// These keys are currently documented for future Redis-backed lockout acceleration.
/// The primary lockout enforcement uses ASP.NET Core Identity's built-in AccessFailedCount
/// and LockoutEnd (persisted to PostgreSQL), which satisfies AC-2 and AC-3 without Redis dependency.
/// </summary>
public static class LockoutRedisKeys
{
    /// <summary>
    /// Atomic failed-login attempt counter.
    /// Pattern: <c>lockout:attempts:{userId}</c>
    /// TTL: 1800 seconds (30 minutes).
    /// Set EXPIRE on first INCR; DEL on successful login.
    /// </summary>
    public static string LockoutAttempts(Guid userId) =>
        $"lockout:attempts:{userId}";

    /// <summary>
    /// Fast-path lockout expiry timestamp (ISO 8601).
    /// Pattern: <c>lockout:until:{userId}</c>
    /// TTL: 1800 seconds (30 minutes).
    /// Not authoritative — Identity's DB-persisted LockoutEnd is the source of truth.
    /// </summary>
    public static string LockoutUntil(Guid userId) =>
        $"lockout:until:{userId}";

    /// <summary>
    /// Per-MFA-token verify attempt counter.
    /// Pattern: <c>mfa:rate:{mfaTokenJti}</c>
    /// TTL: 300 seconds (5 minutes — matches mfaToken expiry).
    /// Used for secondary per-token brute-force prevention alongside the IP-based rate limiter.
    /// </summary>
    public static string MfaRateLimit(string mfaTokenJti) =>
        $"mfa:rate:{mfaTokenJti}";
}
