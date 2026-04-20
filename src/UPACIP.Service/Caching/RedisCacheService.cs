using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;

namespace UPACIP.Service.Caching;

/// <summary>
/// IDistributedCache wrapper implementing the cache-aside pattern with:
///   – System.Text.Json serialization
///   – 5-minute default sliding expiration (NFR-030)
///   – Polly circuit breaker: opens after 3 consecutive failures, resets after 30 s
///   – Graceful fallback on any cache error — cache failures never propagate to callers (AC-4)
/// </summary>
public sealed class RedisCacheService : ICacheService
{
    // Default TTL applied when the caller does not specify an explicit expiration (NFR-030).
    private static readonly TimeSpan DefaultExpiration = TimeSpan.FromMinutes(5);

    private readonly IDistributedCache _cache;
    private readonly ILogger<RedisCacheService> _logger;
    private readonly AsyncCircuitBreakerPolicy _circuitBreaker;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
    };

    public RedisCacheService(IDistributedCache cache, ILogger<RedisCacheService> logger)
    {
        _cache = cache;
        _logger = logger;

        // Circuit breaker: open after 3 consecutive exceptions; half-open after 30 s.
        // When open, BrokenCircuitException is thrown immediately — caught in each method below.
        _circuitBreaker = Policy
            .Handle<Exception>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 3,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (ex, duration) =>
                    _logger.LogWarning(ex,
                        "Redis circuit breaker opened. Cache calls will bypass Redis for {Duration}.",
                        duration),
                onReset: () =>
                    _logger.LogInformation("Redis circuit breaker reset. Cache calls resumed."),
                onHalfOpen: () =>
                    _logger.LogInformation("Redis circuit breaker half-open. Testing connection."));
    }

    /// <inheritdoc/>
    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default)
        where T : class
    {
        try
        {
            var bytes = await _circuitBreaker.ExecuteAsync(
                ct => _cache.GetAsync(key, ct), cancellationToken);

            if (bytes is null || bytes.Length == 0)
                return null;

            return JsonSerializer.Deserialize<T>(bytes, JsonOptions);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("Cache GET bypassed — circuit open. Key: {Key}", key);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache GET failed for key {Key}. Returning null.", key);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        try
        {
            var ttl = expiration ?? DefaultExpiration;
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            var cacheOptions = new DistributedCacheEntryOptions
            {
                // Sliding expiration restarts the TTL window on each access (NFR-030 intent).
                SlidingExpiration = ttl,
            };

            await _circuitBreaker.ExecuteAsync(
                ct => _cache.SetAsync(key, bytes, cacheOptions, ct), cancellationToken);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("Cache SET bypassed — circuit open. Key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache SET failed for key {Key}. Value not cached.", key);
        }
    }

    /// <inheritdoc/>
    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            await _circuitBreaker.ExecuteAsync(
                ct => _cache.RemoveAsync(key, ct), cancellationToken);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("Cache REMOVE bypassed — circuit open. Key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cache REMOVE failed for key {Key}.", key);
        }
    }

    /// <inheritdoc/>
    public async Task<T?> GetOrSetAsync<T>(string key, Func<Task<T?>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
        where T : class
    {
        // 1. Try cache first.
        var cached = await GetAsync<T>(key, cancellationToken);
        if (cached is not null)
            return cached;

        // 2. Cache miss (or circuit open) — invoke the factory to fetch from the source of truth.
        var value = await factory();

        // 3. Populate cache asynchronously; swallowed on error so the caller always gets a result.
        if (value is not null)
            await SetAsync(key, value, expiration, cancellationToken);

        return value;
    }
}
