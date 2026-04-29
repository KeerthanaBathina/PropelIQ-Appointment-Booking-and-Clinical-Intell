namespace UPACIP.Service.Resilience.Models;

/// <summary>
/// Strongly-typed binding for the "Resilience" section in appsettings.json.
///
/// Configures the three named Polly V8 resilience pipelines:
///   - DatabaseRetryPipeline: transient DB retry (AC-2, NFR-032).
///   - HttpRetryPipeline: transient HTTP retry + circuit breaker (AC-2, NFR-023).
///   - ExternalServiceRetryPipeline: IO/socket retry + circuit breaker (AC-2, NFR-023).
///
/// Required appsettings.json section:
/// <code>
/// "Resilience": {
///   "MaxRetries": 3,
///   "RetryDelaysSeconds": [1.0, 5.0, 15.0],
///   "CircuitBreakerFailureThreshold": 5,
///   "CircuitBreakerBreakDurationSeconds": 30,
///   "CircuitBreakerSamplingDurationSeconds": 60,
///   "HttpTimeoutSeconds": 30,
///   "EnableRetryLogging": true
/// }
/// </code>
/// </summary>
public sealed class ResilienceOptions
{
    public const string SectionName = "Resilience";

    /// <summary>Maximum retry attempts per transient failure (AC-2, NFR-032). Default: 3.</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Fixed retry delays in seconds for each attempt (index 0 = first retry delay).
    /// Must have exactly <see cref="MaxRetries"/> entries. Default: [1.0, 5.0, 15.0] (AC-2).
    /// </summary>
    public double[] RetryDelaysSeconds { get; init; } = [1.0, 5.0, 15.0];

    /// <summary>
    /// Consecutive failures required to open the circuit breaker (NFR-023). Default: 5.
    /// </summary>
    public int CircuitBreakerFailureThreshold { get; init; } = 5;

    /// <summary>
    /// Duration in seconds the circuit stays open before transitioning to HalfOpen for a
    /// probe request (NFR-023). Default: 30 seconds.
    /// </summary>
    public int CircuitBreakerBreakDurationSeconds { get; init; } = 30;

    /// <summary>
    /// Sampling window in seconds for circuit breaker failure counting. Default: 60 seconds.
    /// </summary>
    public int CircuitBreakerSamplingDurationSeconds { get; init; } = 60;

    /// <summary>Per-attempt timeout for external HTTP calls in seconds. Default: 30.</summary>
    public int HttpTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// When true, each retry attempt is logged at Warning level with attempt number,
    /// delay, exception type, and correlation ID.
    /// </summary>
    public bool EnableRetryLogging { get; init; } = true;

    /// <summary>
    /// Returns the retry delay for <paramref name="attemptNumber"/> (0-based).
    /// Clamps to the last delay if out of range.
    /// </summary>
    public TimeSpan GetRetryDelay(int attemptNumber)
    {
        var idx = Math.Min(attemptNumber, RetryDelaysSeconds.Length - 1);
        return TimeSpan.FromSeconds(RetryDelaysSeconds[idx]);
    }
}
