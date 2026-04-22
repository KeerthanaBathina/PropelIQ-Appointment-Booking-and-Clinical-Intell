using Microsoft.Extensions.Logging;
using UPACIP.Service.Caching;
using UPACIP.Service.Consolidation;

namespace UPACIP.Service.AI;

/// <summary>
/// Redis-backed AI availability monitoring service (US_046 AC-4, NFR-030).
///
/// Redis key: <c>upacip:ai:health</c>
/// TTL: 5 minutes (NFR-030 — cache-TTL ceiling).
///
/// When the circuit breaker opens in the AI pipeline services, they call
/// <see cref="SetUnavailableAsync"/> to propagate failure state immediately. The status
/// expires after 5 minutes so the system transitions back to "available" without
/// requiring an explicit recovery call (fail-open design for AI availability).
///
/// Thread safety: Redis operations via <see cref="ICacheService"/> are independently
/// atomic; no additional locking is required at this layer.
/// </summary>
public sealed class AiHealthCheckService : IAiHealthCheckService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    internal const string CacheKey = "upacip:ai:health";
    private  static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ICacheService               _cache;
    private readonly ILogger<AiHealthCheckService> _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public AiHealthCheckService(ICacheService cache, ILogger<AiHealthCheckService> logger)
    {
        _cache  = cache;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IAiHealthCheckService — GetHealthStatusAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<AiHealthStatusDto> GetHealthStatusAsync(CancellationToken ct = default)
    {
        var cached = await _cache.GetAsync<AiHealthStatusDto>(CacheKey, ct);
        if (cached is not null)
        {
            _logger.LogDebug("AiHealthCheckService: returning cached AI health status. IsAvailable={IsAvailable}", cached.IsAvailable);
            return cached;
        }

        // No cached entry — assume available (fail-open), cache and return.
        // The AI pipeline will call SetUnavailableAsync when a failure actually occurs.
        var fresh = new AiHealthStatusDto { IsAvailable = true, CheckedAt = DateTimeOffset.UtcNow };
        await _cache.SetAsync(CacheKey, fresh, CacheTtl, ct);

        _logger.LogDebug("AiHealthCheckService: no cached status found; defaulting to available.");
        return fresh;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IAiHealthCheckService — SetUnavailableAsync / SetAvailableAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task SetUnavailableAsync(string reason, CancellationToken ct = default)
    {
        var status = new AiHealthStatusDto
        {
            IsAvailable = false,
            CheckedAt   = DateTimeOffset.UtcNow,
            Reason      = reason,
        };

        await _cache.SetAsync(CacheKey, status, CacheTtl, ct);

        _logger.LogWarning("AiHealthCheckService: AI marked unavailable. Reason={Reason}", reason);
    }

    /// <inheritdoc />
    public async Task SetAvailableAsync(CancellationToken ct = default)
    {
        var status = new AiHealthStatusDto { IsAvailable = true, CheckedAt = DateTimeOffset.UtcNow };
        await _cache.SetAsync(CacheKey, status, CacheTtl, ct);

        _logger.LogInformation("AiHealthCheckService: AI marked available.");
    }
}
