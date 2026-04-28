namespace UPACIP.Service.Resilience.Models;

/// <summary>
/// Current state of a Polly V8 circuit breaker (US_084 task_001, AC-1).
/// Named <c>ProviderCircuitState</c> to avoid collision with
/// <c>Polly.CircuitBreaker.CircuitState</c>.
/// </summary>
public enum ProviderCircuitState
{
    /// <summary>Circuit closed — calls pass through normally.</summary>
    Closed,

    /// <summary>Circuit open — calls are short-circuited immediately.</summary>
    Open,

    /// <summary>Circuit half-open — a single probe request is allowed through to test recovery.</summary>
    HalfOpen,
}
