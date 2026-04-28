namespace UPACIP.Service.AiSafety.Models;

/// <summary>
/// Result returned by <see cref="IAiRateLimiter.CheckRateLimitAsync"/> and
/// <see cref="IAiRateLimiter.GetRemainingQuotaAsync"/> (US_079 task_003, AC-4).
/// </summary>
public sealed class RateLimitResult
{
    /// <summary>
    /// <see langword="true"/> when the request is within quota and should proceed;
    /// <see langword="false"/> when the limit has been reached and the caller must
    /// return HTTP 429.
    /// </summary>
    public bool IsAllowed { get; init; }

    /// <summary>
    /// Remaining requests in the current sliding window.
    /// Zero when <see cref="IsAllowed"/> is <see langword="false"/>.
    /// </summary>
    public int RemainingRequests { get; init; }

    /// <summary>
    /// Seconds until the oldest request in the window expires and a new slot becomes
    /// available. Zero when <see cref="IsAllowed"/> is <see langword="true"/>.
    /// </summary>
    public int RetryAfterSeconds { get; init; }

    /// <summary>Total request count within the current sliding window (including this one).</summary>
    public int CurrentCount { get; init; }

    /// <summary>User ID the result applies to.</summary>
    public string UserId { get; init; } = string.Empty;

    /// <summary>Role used for limit resolution.</summary>
    public string UserRole { get; init; } = string.Empty;

    /// <summary>
    /// The limit that was checked against.  Equals the role default unless an admin
    /// temporary override is active, in which case it reflects the override value.
    /// </summary>
    public int AppliedLimit { get; init; }
}
