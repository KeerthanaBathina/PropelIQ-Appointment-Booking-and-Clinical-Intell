using Microsoft.Extensions.Logging;

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
/// </summary>
public sealed class RedisSessionService : ISessionService
{
    private static readonly TimeSpan SessionTtl = TimeSpan.FromMinutes(15);
    private const string KeyPrefix = "session:";

    private readonly Caching.ICacheService _cache;
    private readonly ILogger<RedisSessionService> _logger;

    public RedisSessionService(Caching.ICacheService cache, ILogger<RedisSessionService> logger)
    {
        _cache  = cache;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task CreateSessionAsync(
        string userId,
        string sessionId,
        string ipAddress,
        string userAgent,
        CancellationToken cancellationToken = default)
    {
        var data = new SessionData
        {
            SessionId    = sessionId,
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
        var session = await _cache.GetAsync<SessionData>(BuildKey(userId), cancellationToken);
        return session is not null;
    }

    private static string BuildKey(string userId) => $"{KeyPrefix}{userId}";
}
