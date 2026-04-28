namespace UPACIP.Service.Resilience.Models;

/// <summary>
/// Structured fallback result returned by resilient service wrappers when the circuit
/// breaker is open or retries are exhausted (US_084 task_001, AC-1).
/// </summary>
public sealed record ExternalServiceFallback
{
    /// <summary>The dependency that triggered the fallback (e.g. <c>"Sms"</c>, <c>"Email"</c>).</summary>
    public string DependencyName { get; init; } = string.Empty;

    /// <summary>Short code describing the fallback taken (e.g. <c>"queued_for_retry"</c>).</summary>
    public string FallbackAction { get; init; } = string.Empty;

    /// <summary><c>true</c> when the notification has been enqueued in Redis for later re-delivery.</summary>
    public bool QueuedForRetry { get; init; }

    /// <summary>Human-readable description of the fallback outcome.</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary><c>true</c> when the call succeeded without requiring a fallback.</summary>
    public bool Success { get; init; }

    /// <summary>Creates a success result (no fallback required).</summary>
    public static ExternalServiceFallback Succeeded(string dependency) =>
        new() { DependencyName = dependency, Success = true, FallbackAction = "none", Message = "Delivered." };

    /// <summary>Creates a queued-for-retry fallback result.</summary>
    public static ExternalServiceFallback QueuedRetry(string dependency) =>
        new()
        {
            DependencyName = dependency,
            Success        = false,
            FallbackAction = "queued_for_retry",
            QueuedForRetry = true,
            Message        = $"{dependency} delivery temporarily unavailable — queued for retry.",
        };
}
