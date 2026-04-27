using System.ComponentModel.DataAnnotations;

namespace UPACIP.Api.Features.AIGateway.Configuration;

/// <summary>
/// Polly V8 resilience policy configuration for the AI Gateway provider pipeline
/// (US_067 TASK_002, AIR-O04, AIR-O08).
///
/// Bound from the <c>"AIGateway:Resilience"</c> configuration section.
///
/// Per-provider resilience strategy applied in order (outermost → innermost):
///   1. Circuit Breaker — open after <see cref="CircuitBreakerFailureThreshold"/>
///      failures within a 30-second sampling window; break for
///      <see cref="CircuitBreakerBreakDurationSeconds"/> (AIR-O04).
///   2. Retry — exponential backoff up to <see cref="RetryMaxAttempts"/> attempts (AIR-O08).
/// </summary>
public sealed class ResilienceOptions
{
    public const string SectionName = "AIGateway:Resilience";

    /// <summary>
    /// Minimum number of failures within the sampling window required to open the
    /// circuit breaker for a provider (AIR-O04 = 5 consecutive failures).
    /// Maps to <c>MinimumThroughput</c> in <c>CircuitBreakerStrategyOptions</c> with
    /// <c>FailureRatio = 1.0</c> so ALL requests in the window must fail.
    /// </summary>
    [Range(1, 100)]
    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    /// <summary>
    /// Duration in seconds the circuit breaker remains open before transitioning
    /// to half-open for a probe request (AIR-O04 = 30 s).
    /// </summary>
    [Range(1, 300)]
    public int CircuitBreakerBreakDurationSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum number of retry attempts per provider before exhausting the pipeline.
    /// Uses exponential backoff with jitter (AIR-O08 = 3 retries).
    /// </summary>
    [Range(0, 10)]
    public int RetryMaxAttempts { get; set; } = 3;

    /// <summary>
    /// Base delay in seconds for exponential backoff between retries.
    /// Actual delay = base * 2^attempt + jitter. Defaults to 1 s.
    /// </summary>
    [Range(1, 30)]
    public int RetryBaseDelaySeconds { get; set; } = 1;
}
