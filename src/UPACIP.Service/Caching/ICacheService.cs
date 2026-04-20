namespace UPACIP.Service.Caching;

/// <summary>
/// Generic distributed cache abstraction for the UPACIP platform.
/// All feature services should consume this interface rather than IDistributedCache directly.
/// </summary>
public interface ICacheService
{
    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, or <c>null</c> if not present or Redis is unavailable.
    /// </summary>
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Stores <paramref name="value"/> in the cache under <paramref name="key"/> with an optional
    /// explicit expiration. When <paramref name="expiration"/> is omitted the default 5-minute TTL applies (NFR-030).
    /// Failures are swallowed — cache write errors never break the request pipeline.
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
        where T : class;

    /// <summary>
    /// Removes the cache entry for <paramref name="key"/>.
    /// Failures are swallowed — cache remove errors never break the request pipeline.
    /// </summary>
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cache-aside pattern: returns the cached value when present; otherwise invokes
    /// <paramref name="factory"/> to produce the value, caches it, and returns it.
    /// When Redis is unavailable the factory is always invoked directly (AC-4 fallback).
    /// </summary>
    Task<T?> GetOrSetAsync<T>(string key, Func<Task<T?>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
        where T : class;
}
