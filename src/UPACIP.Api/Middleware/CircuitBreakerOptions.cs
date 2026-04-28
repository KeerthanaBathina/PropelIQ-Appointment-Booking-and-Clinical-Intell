namespace UPACIP.Api.Middleware;

/// <summary>
/// Configuration for <see cref="EndpointCircuitBreakerMiddleware"/>
/// (US_082 task_001, AC-4 — endpoint circuit-breaker policy).
///
/// <para>Bind from the <c>"CircuitBreaker"</c> section in <c>appsettings.json</c>:</para>
/// <code>
/// "CircuitBreaker": {
///   "CriticalPaths": [ "/api/auth", "/api/appointments/book", "/health", "/ready" ],
///   "StandardFailureThreshold": 10,
///   "NonCriticalFailureThreshold": 5,
///   "HalfOpenRetrySeconds": 15,
///   "NonCriticalBreakDurationSeconds": 30,
///   "StandardBreakDurationSeconds": 15
/// }
/// </code>
/// </summary>
public sealed class CircuitBreakerOptions
{
    public const string SectionName = "CircuitBreaker";

    /// <summary>
    /// URL path prefixes that are considered <i>critical</i> and must NEVER be
    /// circuit-broken. Default: auth, booking, health, and readiness probes.
    /// </summary>
    public string[] CriticalPaths { get; set; } =
    [
        "/api/auth",
        "/api/appointments/book",
        "/health",
        "/ready"
    ];

    /// <summary>
    /// Minimum number of failures in the 30-second sampling window before the
    /// Standard circuit breaker opens. Default: 10.
    /// </summary>
    public int StandardFailureThreshold { get; set; } = 10;

    /// <summary>
    /// Minimum number of failures in the 30-second sampling window before the
    /// NonCritical circuit breaker opens. Default: 5.
    /// </summary>
    public int NonCriticalFailureThreshold { get; set; } = 5;

    /// <summary>
    /// Number of seconds the circuit remains in half-open state before admitting
    /// a probe request. Default: 15.
    /// </summary>
    public int HalfOpenRetrySeconds { get; set; } = 15;

    /// <summary>
    /// Break duration (seconds) for the Standard circuit.
    /// The circuit remains open for this duration before entering half-open. Default: 15.
    /// </summary>
    public int StandardBreakDurationSeconds { get; set; } = 15;

    /// <summary>
    /// Break duration (seconds) for the NonCritical circuit (shed load faster). Default: 30.
    /// </summary>
    public int NonCriticalBreakDurationSeconds { get; set; } = 30;
}
