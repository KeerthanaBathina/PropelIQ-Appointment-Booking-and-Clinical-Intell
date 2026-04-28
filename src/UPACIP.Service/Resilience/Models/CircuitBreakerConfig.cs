namespace UPACIP.Service.Resilience.Models;

/// <summary>
/// Per-dependency Polly V8 resilience policy settings (US_084 task_001, AC-1).
/// </summary>
public sealed class CircuitBreakerConfig
{
    /// <summary>Consecutive failures required to open the circuit.</summary>
    public int FailureThreshold { get; set; } = 3;

    /// <summary>How many seconds the circuit stays open before transitioning to HalfOpen.</summary>
    public int BreakDurationSeconds { get; set; } = 30;

    /// <summary>Per-call timeout in seconds applied as the innermost pipeline strategy.</summary>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>Maximum retry attempts before counting the call as a failure.</summary>
    public int RetryCount { get; set; } = 2;

    /// <summary>Base delay in milliseconds for exponential backoff between retries.</summary>
    public int RetryBaseDelayMs { get; set; } = 500;
}
