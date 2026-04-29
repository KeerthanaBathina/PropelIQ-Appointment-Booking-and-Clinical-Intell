using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace UPACIP.Api.HealthChecks;

/// <summary>
/// Redis dependency health check (US_099, AC-1, AC-3).
///
/// Executes a <c>PING</c> command against the Redis connection multiplexer and measures
/// round-trip latency. Redis is a <b>non-critical</b> dependency — the application
/// degrades gracefully (no caching) when Redis is unavailable:
/// <list type="bullet">
///   <item>PING succeeds within 100 ms → <see cref="HealthCheckResult.Healthy"/>.</item>
///   <item>PING latency &gt; 100 ms → <see cref="HealthCheckResult.Degraded"/> — cache is
///         slow; callers may choose to skip caching for the current request (AC-3).</item>
///   <item>Exception (connection refused, timeout) →
///         <see cref="HealthCheckResult.Degraded"/> — cache unavailable; overall status
///         remains at most <c>Degraded</c>, not <c>Unhealthy</c> (AC-3).</item>
/// </list>
/// </summary>
public sealed class RedisHealthCheck : IHealthCheck
{
    private static readonly TimeSpan SlowLatencyThreshold = TimeSpan.FromMilliseconds(100);

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisHealthCheck> _logger;

    public RedisHealthCheck(IConnectionMultiplexer redis, ILogger<RedisHealthCheck> logger)
    {
        _redis  = redis;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db      = _redis.GetDatabase();
            var latency = await db.PingAsync();

            var data = new Dictionary<string, object>
            {
                ["latency_ms"] = Math.Round(latency.TotalMilliseconds, 2),
                ["server"]     = "Upstash Redis 7.x",
            };

            if (latency > SlowLatencyThreshold)
            {
                return HealthCheckResult.Degraded(
                    $"Redis responding slowly: {latency.TotalMilliseconds:F0}ms (threshold: {SlowLatencyThreshold.TotalMilliseconds:F0}ms)",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                $"Redis PING: {latency.TotalMilliseconds:F0}ms",
                data: data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HEALTH_CHECK_FAILED: Redis health check exception");
            return HealthCheckResult.Degraded(
                "Redis unavailable — cache functionality degraded",
                exception: ex,
                data: new Dictionary<string, object> { ["error"] = ex.Message });
        }
    }
}
