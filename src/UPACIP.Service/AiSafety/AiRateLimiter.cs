using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using UPACIP.Service.AiSafety.Models;

namespace UPACIP.Service.AiSafety;

/// <summary>
/// Redis sorted-set sliding window rate limiter with atomic Lua script execution
/// (US_079 task_003, AC-4, AIR-S08, TR-027).
///
/// <para>
/// <b>Sliding window design (per user)</b>:
/// <list type="bullet">
///   <item>Redis key: <c>ai:ratelimit:{userId}</c> — sorted set where each member is a
///     unique request GUID and the score is the Unix timestamp in milliseconds at the time
///     of the request.</item>
///   <item>Before checking the count, all members with score &lt; <c>now - windowMs</c>
///     are removed (<c>ZREMRANGEBYSCORE</c>).</item>
///   <item>Steps (remove expired → count → conditionally add) execute atomically via a
///     Lua script to prevent race conditions in concurrent requests.</item>
///   <item>Key TTL is set to <c>WindowSizeMinutes + 5</c> minutes for automatic cleanup.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Override key</b>: <c>ai:ratelimit:override:{userId}</c> — JSON string
/// <c>{ "limit": N, "expiresAt": "ISO8601" }</c>; checked before the role default.
/// Redis TTL mirrors the override duration for automatic expiry.
/// </para>
///
/// <para>Scoped lifetime — injected into the ASP.NET Core middleware via method-injection
/// on <c>InvokeAsync</c>.</para>
/// </summary>
public sealed class AiRateLimiter : IAiRateLimiter
{
    // ── Redis key prefixes ────────────────────────────────────────────────────

    private const string WindowKeyPrefix   = "ai:ratelimit:";
    private const string OverrideKeyPrefix = "ai:ratelimit:override:";

    // ── Role name constants (principle of least privilege for unknowns) ───────

    private const string PatientRole = "Patient";
    private const string StaffRole   = "Staff";
    private const string AdminRole   = "Admin";

    // ── Lua script ────────────────────────────────────────────────────────────
    // KEYS[1] = sorted-set key (ai:ratelimit:{userId})
    // ARGV[1] = current Unix timestamp in milliseconds (as string)
    // ARGV[2] = window start timestamp (now - windowMs, as string)
    // ARGV[3] = limit (as string)
    // ARGV[4] = TTL in seconds for the key
    // ARGV[5] = new request GUID (unique member value)
    //
    // Returns a two-element array:
    //   [0] = 1 (allowed) or 0 (denied)
    //   [1] = current count AFTER potential increment

    private const string SlidingWindowLua = @"
redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', ARGV[2])
local count = redis.call('ZCARD', KEYS[1])
local limit = tonumber(ARGV[3])
if count < limit then
    redis.call('ZADD', KEYS[1], ARGV[1], ARGV[5])
    redis.call('EXPIRE', KEYS[1], ARGV[4])
    return {1, count + 1}
else
    return {0, count}
end";

    private readonly IConnectionMultiplexer        _redis;
    private readonly IOptions<RateLimitOptions>    _options;
    private readonly ILogger<AiRateLimiter>        _logger;

    public AiRateLimiter(
        IConnectionMultiplexer      redis,
        IOptions<RateLimitOptions>  options,
        ILogger<AiRateLimiter>      logger)
    {
        _redis   = redis;
        _options = options;
        _logger  = logger;
    }

    /// <inheritdoc/>
    public async Task<RateLimitResult> CheckRateLimitAsync(
        string            userId,
        string            userRole,
        CancellationToken cancellationToken = default)
    {
        var opts         = _options.Value;
        var limit        = await ResolveApplicableLimitAsync(userId, userRole, opts);
        var windowKey    = $"{WindowKeyPrefix}{userId}";
        var nowMs        = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowMs     = (long)opts.WindowSizeMinutes * 60 * 1000;
        var windowStart  = nowMs - windowMs;
        var ttlSeconds   = (opts.WindowSizeMinutes + 5) * 60;
        var requestId    = Guid.NewGuid().ToString("N");

        var db = _redis.GetDatabase();

        RedisResult result;
        try
        {
            result = await db.ScriptEvaluateAsync(
                SlidingWindowLua,
                keys:   [new RedisKey(windowKey)],
                values:
                [
                    (RedisValue)nowMs.ToString(),
                    (RedisValue)windowStart.ToString(),
                    (RedisValue)limit.ToString(),
                    (RedisValue)ttlSeconds.ToString(),
                    (RedisValue)requestId,
                ]);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AiRateLimiter: Redis error during CheckRateLimitAsync for userId={UserId}. " +
                "Allowing request to avoid false positives due to cache failure.",
                userId);

            // Fail open — a Redis outage must not block legitimate users (NFR-030 graceful fallback).
            return new RateLimitResult
            {
                IsAllowed        = true,
                RemainingRequests = limit - 1,
                CurrentCount     = 1,
                UserId           = userId,
                UserRole         = userRole,
                AppliedLimit     = limit,
            };
        }

        var resultArray  = (RedisResult[])result!;
        var isAllowed    = (int)resultArray[0] == 1;
        var currentCount = (int)resultArray[1];

        if (isAllowed)
        {
            return new RateLimitResult
            {
                IsAllowed         = true,
                RemainingRequests = Math.Max(0, limit - currentCount),
                RetryAfterSeconds = 0,
                CurrentCount      = currentCount,
                UserId            = userId,
                UserRole          = userRole,
                AppliedLimit      = limit,
            };
        }

        // Denied: compute RetryAfterSeconds from oldest member in the window.
        var retryAfterSeconds = await ComputeRetryAfterAsync(db, windowKey, nowMs, windowMs);

        return new RateLimitResult
        {
            IsAllowed         = false,
            RemainingRequests = 0,
            RetryAfterSeconds = retryAfterSeconds,
            CurrentCount      = currentCount,
            UserId            = userId,
            UserRole          = userRole,
            AppliedLimit      = limit,
        };
    }

    /// <inheritdoc/>
    public async Task<RateLimitResult> GetRemainingQuotaAsync(
        string            userId,
        string            userRole,
        CancellationToken cancellationToken = default)
    {
        var opts        = _options.Value;
        var limit       = await ResolveApplicableLimitAsync(userId, userRole, opts);
        var windowKey   = $"{WindowKeyPrefix}{userId}";
        var nowMs       = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var windowStart = nowMs - (long)opts.WindowSizeMinutes * 60 * 1000;

        var db = _redis.GetDatabase();

        try
        {
            // Remove expired members then count — read-only from the caller's perspective.
            await db.SortedSetRemoveRangeByScoreAsync(windowKey,
                double.NegativeInfinity, windowStart);

            var count = (int)await db.SortedSetLengthAsync(windowKey);

            return new RateLimitResult
            {
                IsAllowed         = count < limit,
                RemainingRequests = Math.Max(0, limit - count),
                RetryAfterSeconds = 0,
                CurrentCount      = count,
                UserId            = userId,
                UserRole          = userRole,
                AppliedLimit      = limit,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AiRateLimiter: Redis error during GetRemainingQuotaAsync for userId={UserId}.",
                userId);

            return new RateLimitResult
            {
                IsAllowed         = true,
                RemainingRequests = limit,
                CurrentCount      = 0,
                UserId            = userId,
                UserRole          = userRole,
                AppliedLimit      = limit,
            };
        }
    }

    /// <inheritdoc/>
    public async Task SetTemporaryOverrideAsync(
        string            userId,
        int               overrideLimit,
        int               durationMinutes,
        CancellationToken cancellationToken = default)
    {
        var overrideKey = $"{OverrideKeyPrefix}{userId}";
        var expiresAt   = DateTimeOffset.UtcNow.AddMinutes(durationMinutes);

        var payload = JsonSerializer.Serialize(new OverrideEntry
        {
            Limit     = overrideLimit,
            ExpiresAt = expiresAt,
        });

        var db = _redis.GetDatabase();

        try
        {
            await db.StringSetAsync(overrideKey, payload, TimeSpan.FromMinutes(durationMinutes));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AiRateLimiter: Redis error storing override for userId={UserId}.", userId);
            throw;
        }
    }

    /// <summary>
    /// Removes an active temporary override for <paramref name="userId"/>.
    /// No-op if no override exists.
    /// </summary>
    public async Task ClearTemporaryOverrideAsync(string userId)
    {
        var overrideKey = $"{OverrideKeyPrefix}{userId}";
        var db = _redis.GetDatabase();

        try
        {
            await db.KeyDeleteAsync(overrideKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AiRateLimiter: Redis error removing override for userId={UserId}.", userId);
            throw;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<int> ResolveApplicableLimitAsync(
        string           userId,
        string           userRole,
        RateLimitOptions opts)
    {
        // 1. Check for active admin-set temporary override.
        var overrideKey = $"{OverrideKeyPrefix}{userId}";
        var db = _redis.GetDatabase();

        try
        {
            var raw = await db.StringGetAsync(overrideKey);
            if (raw.HasValue)
            {
                var entry = JsonSerializer.Deserialize<OverrideEntry>((string)raw!);
                if (entry is not null && entry.ExpiresAt > DateTimeOffset.UtcNow)
                    return entry.Limit;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "AiRateLimiter: could not read override for userId={UserId}; falling back to role default.",
                userId);
        }

        // 2. Role-based default (unknown roles → PatientLimitPerHour — least privilege).
        return userRole switch
        {
            StaffRole   => opts.StaffLimitPerHour,
            AdminRole   => opts.AdminLimitPerHour,
            PatientRole => opts.PatientLimitPerHour,
            _           => opts.PatientLimitPerHour,
        };
    }

    private static async Task<int> ComputeRetryAfterAsync(
        IDatabase db,
        string    windowKey,
        long      nowMs,
        long      windowMs)
    {
        try
        {
            // Oldest member is the entry that will expire first.
            var oldest = await db.SortedSetRangeByRankWithScoresAsync(windowKey, 0, 0);
            if (oldest.Length > 0)
            {
                var oldestTimestampMs = (long)oldest[0].Score;
                var expiresAtMs       = oldestTimestampMs + windowMs;
                var retryAfterMs      = expiresAtMs - nowMs;
                return retryAfterMs > 0
                    ? (int)Math.Ceiling(retryAfterMs / 1000.0)
                    : 1;
            }
        }
        catch
        {
            // Best-effort — return a safe fallback if Redis call fails.
        }

        return 60; // Fallback: advise caller to retry in 60 seconds.
    }

    // ── Inner types ───────────────────────────────────────────────────────────

    private sealed class OverrideEntry
    {
        public int            Limit     { get; init; }
        public DateTimeOffset ExpiresAt { get; init; }
    }
}
