using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using UPACIP.Service.Queue;

namespace UPACIP.Service.Caching;

/// <summary>
/// <see cref="IQueueCacheService"/> implementation using StackExchange.Redis for granular
/// per-filter cache management (US_053, NFR-004, NFR-030).
///
/// Cache key format:
///   <c>queue:today:{date:yyyyMMdd}:{provider}:{status}:{page}:{pageSize}</c>
///   where "all" is used when a filter is absent (consistent, predictable key space).
///
/// Cache invalidation:
///   Uses <c>IDatabase.KeyDeleteAsync</c> against all keys returned by a SCAN on the pattern
///   <c>queue:today:{date:yyyyMMdd}:*</c>. This is safe for the expected key volume (one page
///   per provider/status combination = &lt;100 keys/day).
///
/// Failures (network errors, circuit breaker) are swallowed — cache errors never surface
/// to API callers (NFR-030 resilience requirement).
/// </summary>
public sealed class QueueCacheService : IQueueCacheService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented               = false,
    };

    private readonly IConnectionMultiplexer       _redis;
    private readonly ILogger<QueueCacheService>   _logger;

    public QueueCacheService(
        IConnectionMultiplexer      redis,
        ILogger<QueueCacheService>  logger)
    {
        _redis  = redis;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<QueuePagedResponseDto?> GetCachedQueueAsync(
        QueueFilterParams filters,
        CancellationToken cancellationToken = default)
    {
        var key = BuildKey(filters);
        try
        {
            var db    = _redis.GetDatabase();
            var value = await db.StringGetAsync(key).WaitAsync(cancellationToken);

            if (value.IsNullOrEmpty)
                return null;

            _logger.LogDebug("QueueCacheService.Get: cache hit for key={Key}.", key);
            return JsonSerializer.Deserialize<QueuePagedResponseDto>(value.ToString(), JsonOpts);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QueueCacheService.Get: failed for key={Key}. Falling through to DB.", key);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task SetCachedQueueAsync(
        QueueFilterParams      filters,
        QueuePagedResponseDto  data,
        CancellationToken cancellationToken = default)
    {
        var key = BuildKey(filters);
        try
        {
            var json = JsonSerializer.Serialize(data, JsonOpts);
            var db   = _redis.GetDatabase();
            await db.StringSetAsync(key, json, CacheTtl).WaitAsync(cancellationToken);
            _logger.LogDebug("QueueCacheService.Set: cached key={Key}, TTL={Ttl}.", key, CacheTtl);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QueueCacheService.Set: failed for key={Key}. Continuing without cache write.", key);
        }
    }

    /// <inheritdoc/>
    public async Task InvalidateQueueCacheAsync(CancellationToken cancellationToken = default)
    {
        var pattern = BuildInvalidationPattern();
        try
        {
            var server  = _redis.GetServer(_redis.GetEndPoints()[0]);
            var db      = _redis.GetDatabase();
            var keys    = server.Keys(pattern: pattern).ToArray();

            if (keys.Length == 0)
                return;

            await db.KeyDeleteAsync(keys).WaitAsync(cancellationToken);
            _logger.LogDebug(
                "QueueCacheService.Invalidate: removed {Count} keys matching pattern={Pattern}.",
                keys.Length, pattern);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "QueueCacheService.Invalidate: failed for pattern={Pattern}. Cache may be stale.", pattern);
        }
    }

    // ─── Key helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a per-filter deterministic cache key.
    /// "all" is used for absent filters so key structure stays consistent.
    /// </summary>
    private static string BuildKey(QueueFilterParams filters)
    {
        var date     = DateTime.UtcNow.ToString("yyyyMMdd");
        var provider = filters.HasProviderFilter ? filters.Provider!.ToLowerInvariant() : "all";
        var status   = filters.HasStatusFilter   ? filters.Status!.ToLowerInvariant()   : "all";
        return $"queue:today:{date}:{provider}:{status}:{filters.Page}:{filters.PageSize}";
    }

    /// <summary>Pattern for today's queue keys (used in SCAN + bulk delete).</summary>
    private static string BuildInvalidationPattern()
    {
        var date = DateTime.UtcNow.ToString("yyyyMMdd");
        return $"queue:today:{date}:*";
    }
}
