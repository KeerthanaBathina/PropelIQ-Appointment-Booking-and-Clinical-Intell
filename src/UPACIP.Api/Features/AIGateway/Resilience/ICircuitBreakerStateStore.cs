using UPACIP.Api.Features.AIGateway.Models;

namespace UPACIP.Api.Features.AIGateway.Resilience;

/// <summary>
/// Distributed storage contract for AI provider circuit breaker state
/// (US_070 TASK_002, AIR-O04, EC-2).
///
/// <para>Because Polly's circuit breaker holds state in-process, multiple instances of
/// the API service each maintain independent circuit breaker instances. This store
/// propagates state transitions to Redis so that all instances observe a consistent view
/// of provider health.</para>
///
/// <para>Workflow:</para>
/// <list type="number">
///   <item>When Polly fires an <c>OnOpened</c>/<c>OnHalfOpened</c>/<c>OnClosed</c>
///   callback, <see cref="CircuitBreakerStateMonitor"/> calls
///   <see cref="SetStateAsync"/> to write the new state to Redis.</item>
///   <item>At request dispatch time, <see cref="AIProviderFallbackHandler"/> calls
///   <see cref="GetStateAsync"/> to read cross-instance state and may skip directly to the
///   fallback provider if Redis indicates the primary circuit is open on another instance.</item>
/// </list>
///
/// Implementations must be thread-safe. The registered lifetime is Singleton.
/// </summary>
public interface ICircuitBreakerStateStore
{
    /// <summary>
    /// Retrieves the current circuit breaker state for <paramref name="providerName"/>
    /// from the distributed store.
    /// Returns <see cref="CircuitBreakerState.Closed"/> when no state has been written
    /// (optimistic default — unknown = healthy).
    /// </summary>
    /// <param name="providerName">Normalized provider identifier (e.g., <c>"openai"</c>).</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    Task<CircuitBreakerState> GetStateAsync(
        string            providerName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes <paramref name="state"/> for <paramref name="providerName"/> to the
    /// distributed store with an optional TTL.
    /// </summary>
    /// <param name="providerName">Normalized provider identifier (e.g., <c>"openai"</c>).</param>
    /// <param name="state">The circuit breaker state to persist.</param>
    /// <param name="expiry">
    /// How long the entry should live in Redis before auto-expiring.
    /// Pass <see langword="null"/> for no expiry (use for <see cref="CircuitBreakerState.Closed"/>
    /// entries so they persist until the next explicit transition).
    /// </param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    Task SetStateAsync(
        string              providerName,
        CircuitBreakerState state,
        TimeSpan?           expiry            = null,
        CancellationToken   cancellationToken = default);
}
