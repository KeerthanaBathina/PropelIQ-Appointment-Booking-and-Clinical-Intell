using UPACIP.Service.AiSafety.Models;

namespace UPACIP.Service.AiSafety;

/// <summary>
/// Distributed AI request rate limiter backed by Redis sorted sets
/// (US_079 task_003, AC-4, AIR-S08, TR-027).
///
/// <para>
/// Three operations are exposed:
/// <list type="bullet">
///   <item><see cref="CheckRateLimitAsync"/> — increments the counter and checks against the
///     role-based (or override) limit; use on every AI endpoint request.</item>
///   <item><see cref="GetRemainingQuotaAsync"/> — read-only quota check; no increment;
///     use for the admin status endpoint.</item>
///   <item><see cref="SetTemporaryOverrideAsync"/> — stores an admin-set per-user override
///     in Redis; automatically expires after <paramref name="durationMinutes"/>.</item>
/// </list>
/// </para>
/// </summary>
public interface IAiRateLimiter
{
    /// <summary>
    /// Atomically increments the request counter for <paramref name="userId"/> and checks
    /// whether the current count is within the applicable limit (role default or admin override).
    /// </summary>
    /// <param name="userId">The authenticated user's identifier.</param>
    /// <param name="userRole">The authenticated user's role (Patient / Staff / Admin).</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>
    /// A <see cref="RateLimitResult"/> where <c>IsAllowed = true</c> means the request
    /// may proceed; <c>IsAllowed = false</c> means HTTP 429 should be returned.
    /// </returns>
    Task<RateLimitResult> CheckRateLimitAsync(
        string            userId,
        string            userRole,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the current quota status for <paramref name="userId"/> without incrementing
    /// the request counter.
    /// </summary>
    /// <param name="userId">The authenticated user's identifier.</param>
    /// <param name="userRole">The authenticated user's role.</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    /// <returns>A <see cref="RateLimitResult"/> reflecting the current (non-incremented) window state.</returns>
    Task<RateLimitResult> GetRemainingQuotaAsync(
        string            userId,
        string            userRole,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a temporary per-user rate limit override in Redis.  The override supersedes
    /// the role-based default for the specified duration.
    /// </summary>
    /// <param name="userId">Target user identifier.</param>
    /// <param name="overrideLimit">New request limit for the window duration.</param>
    /// <param name="durationMinutes">How long the override remains active (1–480 minutes).</param>
    /// <param name="cancellationToken">Propagates caller cancellation.</param>
    Task SetTemporaryOverrideAsync(
        string            userId,
        int               overrideLimit,
        int               durationMinutes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes an active temporary override for <paramref name="userId"/>, immediately
    /// reverting to the role-based default.  No-op if no override exists.
    /// </summary>
    /// <param name="userId">Target user identifier.</param>
    Task ClearTemporaryOverrideAsync(string userId);
}
