using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace UPACIP.Service.Auth;

/// <summary>
/// Redis-backed implementation of <see cref="ISessionService"/>.
///
/// Storage: key <c>session:{userId}</c> → <see cref="SessionData"/> JSON.
/// TTL: 15-minute sliding window; every <see cref="UpdateActivityAsync"/> call resets the
/// expiry via <see cref="ICacheService.SetAsync"/> (atomic Redis SET with new absolute TTL).
///
/// Graceful degradation: all Redis errors are caught and logged — session failures never
/// block authenticated requests (circuit breaker from <see cref="Caching.RedisCacheService"/>
/// propagates graceful null-returns upward per NFR-023).
///
/// <c>IConnectionMultiplexer</c> is injected for raw Redis operations (TTL query, atomic
/// GET-DELETE for termination flags) that <see cref="Caching.ICacheService"/> does not expose.
/// The cache instance name prefix (<c>upacip:</c>) is applied to all raw keys so they match
/// what <see cref="IDistributedCache"/> stores.
/// </summary>
public sealed class RedisSessionService : ISessionService
{
    private static readonly TimeSpan SessionTtl            = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan TerminationFlagTtl    = TimeSpan.FromMinutes(5);

    // Key templates — raw Redis keys use the same "upacip:" instance prefix as IDistributedCache.
    private const string KeyPrefix              = "session:";
    private const string RedisInstancePrefix    = "upacip:";
    private const string TerminationFlagPrefix  = "session_terminated:";

    private readonly Caching.ICacheService        _cache;
    private readonly IConnectionMultiplexer        _redis;
    private readonly ILogger<RedisSessionService>  _logger;

    public RedisSessionService(
        Caching.ICacheService       cache,
        IConnectionMultiplexer      redis,
        ILogger<RedisSessionService> logger)
    {
        _cache  = cache;
        _redis  = redis;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task CreateSessionAsync(
        string userId,
        string sessionId,
        string jti,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        var data = new SessionData
        {
            SessionId    = sessionId,
            Jti          = jti,
            LastActivity = DateTime.UtcNow,
            LoginAt      = DateTime.UtcNow,
            IpAddress    = ipAddress,
            UserAgent    = userAgent,
        };

        await _cache.SetAsync(BuildKey(userId), data, SessionTtl, cancellationToken);
        _logger.LogInformation(
            "Session created for user {UserId}. SessionId={SessionId}.", userId, sessionId);
    }

    /// <inheritdoc/>
    public async Task UpdateActivityAsync(string userId, CancellationToken cancellationToken = default)
    {
        var existing = await _cache.GetAsync<SessionData>(BuildKey(userId), cancellationToken);
        if (existing is null)
            return; // Session already expired — no-op; middleware will return 401 on its own check.

        existing.LastActivity = DateTime.UtcNow;

        // Re-store with fresh 15-minute TTL (atomic Redis SET resets the sliding window — AC-2).
        await _cache.SetAsync(BuildKey(userId), existing, SessionTtl, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<SessionData?> GetSessionAsync(string userId, CancellationToken cancellationToken = default)
        => _cache.GetAsync<SessionData>(BuildKey(userId), cancellationToken);

    /// <inheritdoc/>
    public async Task InvalidateSessionAsync(string userId, CancellationToken cancellationToken = default)
    {
        await _cache.RemoveAsync(BuildKey(userId), cancellationToken);
        _logger.LogInformation("Session invalidated for user {UserId}.", userId);
    }

    /// <inheritdoc/>
    public async Task<bool> IsSessionActiveAsync(string userId, CancellationToken cancellationToken = default)
    {
        // Fail-open: if Redis is not connected, allow the request to proceed so that a Redis
        // outage does not lock all authenticated users out (NFR-023 graceful degradation).
        if (!_redis.IsConnected)
        {
            _logger.LogWarning(
                "IsSessionActiveAsync: Redis not connected. Treating session as active for user {UserId}.",
                userId);
            return true;
        }

        var session = await _cache.GetAsync<SessionData>(BuildKey(userId), cancellationToken);
        return session is not null;
    }

    /// <inheritdoc/>
    public async Task<SessionTerminationResult> TerminateAndReplaceSessionAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        // ── Get the existing session ──────────────────────────────────────────────────────────
        SessionData? existing;
        try
        {
            existing = await _cache.GetAsync<SessionData>(BuildKey(userId), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "TerminateAndReplaceSessionAsync: could not read existing session for user {UserId}.",
                userId);
            return new SessionTerminationResult { WasTerminated = false };
        }

        if (existing is null)
            return new SessionTerminationResult { WasTerminated = false };

        // ── Delete the old session ────────────────────────────────────────────────────────────
        await _cache.RemoveAsync(BuildKey(userId), cancellationToken);

        // ── Store the one-time termination notification flag (5-min TTL) ─────────────────────
        // Keyed by the old JWT jti so that the middleware can identify exactly which token
        // was invalidated without false-positives on the new Device B session.
        if (!string.IsNullOrEmpty(existing.Jti))
        {
            try
            {
                var db      = _redis.GetDatabase();
                var flagKey = BuildTerminationFlagKey(existing.Jti);
                var payload = $"{{\"reason\":\"concurrent_login\",\"terminatedAt\":\"{DateTime.UtcNow:O}\"}}";
                await db.StringSetAsync(flagKey, payload, TerminationFlagTtl);
            }
            catch (Exception ex) when (ex is RedisException or RedisTimeoutException or RedisConnectionException)
            {
                // Non-fatal — the session was already deleted. Log and continue (fail-open).
                _logger.LogWarning(ex,
                    "TerminateAndReplaceSessionAsync: could not set termination flag for jti {Jti}.",
                    existing.Jti);
            }
        }

        _logger.LogWarning(
            "Session {OldSessionId} (jti={OldJti}) terminated for user {UserId} " +
            "due to concurrent login. Old device IP: {OldIp}.",
            existing.SessionId, existing.Jti, userId, existing.IpAddress);

        return new SessionTerminationResult
        {
            WasTerminated  = true,
            OldSessionId   = existing.SessionId,
            OldJti         = existing.Jti,
            OldIpAddress   = existing.IpAddress,
            OldUserAgent   = existing.UserAgent,
        };
    }

    /// <inheritdoc/>
    public async Task<int?> GetTimeRemainingAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            var db  = _redis.GetDatabase();
            var ttl = await db.KeyTimeToLiveAsync(BuildRawKey(userId));

            if (ttl is null || ttl <= TimeSpan.Zero)
                return null;

            return (int)ttl.Value.TotalSeconds;
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException or RedisConnectionException)
        {
            _logger.LogWarning(ex,
                "GetTimeRemainingAsync: Redis unavailable for user {UserId}.", userId);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<string?> CheckAndClearTerminationFlagAsync(
        string jti, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(jti))
            return null;

        try
        {
            var db  = _redis.GetDatabase();
            // GETDEL: atomically reads the value and deletes the key (Redis 6.2+, SE.Redis 2.6+).
            var val = await db.StringGetDeleteAsync(BuildTerminationFlagKey(jti));
            return val.IsNullOrEmpty ? null : (string?)val;
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException or RedisConnectionException)
        {
            _logger.LogWarning(ex,
                "CheckAndClearTerminationFlagAsync: Redis unavailable for jti {Jti}.", jti);
            return null;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────────────────────

    private static string BuildKey(string userId)    => $"{KeyPrefix}{userId}";

    /// <summary>
    /// Builds the fully-qualified Redis key as IDistributedCache stores it (with InstanceName prefix).
    /// Used for raw IConnectionMultiplexer operations (TTL query) that bypass IDistributedCache.
    /// </summary>
    private static string BuildRawKey(string userId) => $"{RedisInstancePrefix}{KeyPrefix}{userId}";

    private static string BuildTerminationFlagKey(string jti)
        => $"{RedisInstancePrefix}{TerminationFlagPrefix}{jti}";
}

