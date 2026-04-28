namespace UPACIP.Service.Resilience.Models;

/// <summary>
/// Root configuration object for all external-service resilience pipelines.
/// Bound from the <c>"ExternalServiceResilience"</c> section in <c>appsettings.json</c>
/// (US_084 task_001, AC-1).
/// </summary>
public sealed class ExternalServiceResilienceOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "ExternalServiceResilience";

    /// <summary>
    /// Per-dependency circuit breaker configurations.  Keys: <c>Sms</c>, <c>Email</c>,
    /// <c>AiPrimary</c>, <c>AiFallback</c> (case-insensitive).
    /// </summary>
    public Dictionary<string, CircuitBreakerConfig> Dependencies { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
}
