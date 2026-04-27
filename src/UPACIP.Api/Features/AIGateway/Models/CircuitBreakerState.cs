namespace UPACIP.Api.Features.AIGateway.Models;

/// <summary>
/// Distributed circuit breaker state for an AI provider (US_070 TASK_002, AIR-O04, EC-2).
///
/// <para>This enum represents the observable state shared across all service instances via
/// Redis. It maps to the Polly circuit breaker lifecycle (Closed → Open → HalfOpen → Closed)
/// and adds a <see cref="Degraded"/> state for the dual-provider-failure edge case (EC-1).</para>
///
/// <para>State transitions driven by Polly circuit breaker callbacks:</para>
/// <code>
///   Closed   → Open       (MinimumThroughput consecutive failures)
///   Open     → HalfOpen   (BreakDuration elapsed)
///   HalfOpen → Closed     (probe request succeeded)
///   HalfOpen → Open       (probe request failed)
///   *        → Degraded   (all configured providers are Open simultaneously — EC-1)
/// </code>
/// </summary>
public enum CircuitBreakerState
{
    /// <summary>
    /// Normal operation — circuit is closed and requests route to this provider.
    /// Corresponds to <see cref="Resilience.ProviderState.Active"/>.
    /// </summary>
    Closed = 0,

    /// <summary>
    /// Circuit is open — all new requests are immediately rejected by Polly and routed
    /// to the fallback provider. The break duration countdown is in progress.
    /// Corresponds to <see cref="Resilience.ProviderState.Unavailable"/>.
    /// </summary>
    Open = 1,

    /// <summary>
    /// Circuit is half-open — a single probe request has been allowed through to test
    /// recovery. If the probe succeeds the circuit closes; if it fails the circuit re-opens.
    /// Corresponds to <see cref="Resilience.ProviderState.Degraded"/>.
    /// </summary>
    HalfOpen = 2,

    /// <summary>
    /// All configured providers (primary and fallback) are simultaneously open.
    /// The AI Gateway returns a structured 503 error to callers and emits an admin alert.
    /// Applies to the composite gateway, not to an individual provider.
    /// </summary>
    Degraded = 3,
}
