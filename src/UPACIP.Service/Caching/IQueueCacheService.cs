namespace UPACIP.Service.Caching;

/// <summary>
/// Queue-domain cache abstraction for the arrival queue dashboard (US_053, NFR-004, NFR-030).
///
/// Uses per-filter granular cache keys so each filter/page combination is independently cached:
///   <c>queue:today:{date}:{provider}:{status}:{page}:{pageSize}</c>
///
/// Cache invalidation (called by QueueService on any mutation) uses a Redis SCAN pattern
/// against <c>queue:today:{date}:*</c> to remove all variants atomically via
/// <see cref="IConnectionMultiplexer"/> (StackExchange.Redis).
///
/// All methods swallow cache exceptions (cache failures never break the request pipeline per NFR-030).
/// </summary>
public interface IQueueCacheService
{
    /// <summary>
    /// Attempts to retrieve a cached <see cref="UPACIP.Service.Queue.QueuePagedResponseDto"/>
    /// for the given filter combination. Returns <c>null</c> on miss or when Redis is unavailable.
    /// </summary>
    Task<UPACIP.Service.Queue.QueuePagedResponseDto?> GetCachedQueueAsync(
        UPACIP.Service.Queue.QueueFilterParams filters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores a paged queue response in Redis under the filter-specific cache key
    /// with a fixed 5-minute TTL (NFR-030).
    /// </summary>
    Task SetCachedQueueAsync(
        UPACIP.Service.Queue.QueueFilterParams          filters,
        UPACIP.Service.Queue.QueuePagedResponseDto      data,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes ALL queue cache keys for today (pattern <c>queue:today:{date}:*</c>).
    /// Called by the service layer whenever a queue mutation occurs so the next read
    /// goes to the database and re-populates the cache.
    /// </summary>
    Task InvalidateQueueCacheAsync(CancellationToken cancellationToken = default);
}
