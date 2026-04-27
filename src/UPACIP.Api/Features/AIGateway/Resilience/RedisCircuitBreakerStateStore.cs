using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using UPACIP.Api.Features.AIGateway.Models;

namespace UPACIP.Api.Features.AIGateway.Resilience;

/// <summary>
/// Redis-backed <see cref="ICircuitBreakerStateStore"/> that persists AI provider circuit
/// breaker state using atomic <c>SET</c>/<c>GET</c> operations (US_070 TASK_002, AIR-O04, EC-2).
///
/// <para><strong>Key pattern:</strong> <c>ai-gateway:circuit:{providerName}:state</c></para>
///
/// <para><strong>Cross-instance consistency:</strong> Each call to
/// <see cref="SetStateAsync"/> uses <c>StringSetAsync</c> with an optional TTL so that
/// when a circuit opens on Instance A, Instance B reads the same <c>Open</c> state from
/// Redis and routes its next request directly to the fallback provider without waiting for
/// its own local Polly circuit to trip (EC-2).</para>
///
/// <para><strong>TTL strategy:</strong></para>
/// <list type="bullet">
///   <item><c>Open</c> — TTL equal to the circuit break duration (default 30 s). After TTL
///   the key expires and all instances default to <c>Closed</c>, which aligns with Polly's
///   local half-open probe timing.</item>
///   <item><c>HalfOpen</c> — Short TTL (10 s) to avoid stale half-open state.
///   Polly's probe will close or re-open locally within milliseconds.</item>
///   <item><c>Closed</c> — No TTL; persists until the next explicit transition.</item>
///   <item><c>Degraded</c> — No TTL; requires explicit recovery transition to clear.</item>
/// </list>
///
/// <para>Failures to communicate with Redis are caught and logged as warnings; they do
/// <em>not</em> throw to callers — the AI Gateway falls back to local Polly state
/// gracefully (defense-in-depth, no hard dependency on Redis for basic operation).</para>
///
/// Registered as Singleton — <see cref="IConnectionMultiplexer"/> is also Singleton.
/// </summary>
public sealed class RedisCircuitBreakerStateStore : ICircuitBreakerStateStore
{
    private const string KeyPrefix = "ai-gateway:circuit:";
    private const string KeySuffix = ":state";

    private readonly IConnectionMultiplexer                 _redis;
    private readonly ILogger<RedisCircuitBreakerStateStore> _logger;

    public RedisCircuitBreakerStateStore(
        IConnectionMultiplexer                  redis,
        ILogger<RedisCircuitBreakerStateStore>  logger)
    {
        _redis  = redis;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<CircuitBreakerState> GetStateAsync(
        string            providerName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var db    = _redis.GetDatabase();
            var key   = BuildKey(providerName);
            var value = await db.StringGetAsync(key);

            if (value.IsNullOrEmpty)
                return CircuitBreakerState.Closed; // Default: optimistic healthy

            if (Enum.TryParse<CircuitBreakerState>(value!, ignoreCase: true, out var parsed))
                return parsed;

            _logger.LogWarning(
                "RedisCircuitBreakerStateStore: unrecognised state value '{Value}' for " +
                "provider '{Provider}'. Defaulting to Closed.",
                value, providerName);

            return CircuitBreakerState.Closed;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Redis unavailable — fall back to local Polly state gracefully.
            _logger.LogWarning(
                ex,
                "RedisCircuitBreakerStateStore: failed to read state for provider '{Provider}'. " +
                "Falling back to local circuit state.",
                providerName);

            return CircuitBreakerState.Closed;
        }
    }

    /// <inheritdoc/>
    public async Task SetStateAsync(
        string              providerName,
        CircuitBreakerState state,
        TimeSpan?           expiry            = null,
        CancellationToken   cancellationToken = default)
    {
        try
        {
            var db    = _redis.GetDatabase();
            var key   = BuildKey(providerName);
            var value = state.ToString();

            await db.StringSetAsync(key, value, expiry);

            _logger.LogDebug(
                "RedisCircuitBreakerStateStore: state written. " +
                "Provider={Provider} State={State} TTL={TTL}",
                providerName, state, expiry?.ToString() ?? "none");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Redis unavailable — log and continue. Local Polly state remains authoritative.
            _logger.LogWarning(
                ex,
                "RedisCircuitBreakerStateStore: failed to write state for provider '{Provider}'. " +
                "Cross-instance circuit state may be temporarily inconsistent.",
                providerName);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string BuildKey(string providerName) =>
        $"{KeyPrefix}{providerName.ToLowerInvariant()}{KeySuffix}";
}
