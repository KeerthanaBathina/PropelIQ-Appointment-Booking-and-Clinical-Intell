using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace UPACIP.Api.HealthChecks;

/// <summary>
/// Readiness probe that tracks application startup state (US_099, AC-2, edge case 2).
///
/// Returns <see cref="HealthCheckResult.Unhealthy"/> until <see cref="MarkReady"/> is called,
/// which happens via the <c>IHostApplicationLifetime.ApplicationStarted</c> event in
/// <c>Program.cs</c> — after the HTTP server is listening, all hosted services have started,
/// and the database connection has been verified.
///
/// The <c>/ready</c> endpoint maps this check:
/// <list type="bullet">
///   <item>During startup → HTTP 503 Service Unavailable (AC-2).</item>
///   <item>After <see cref="MarkReady"/> → HTTP 200 OK (AC-2).</item>
/// </list>
///
/// Rolling deployment (edge case 2): Load balancers poll <c>/ready</c>.  The new instance
/// returns 503 until fully initialised; the old instance continues serving traffic.  Once
/// the new instance returns 200, the load balancer shifts traffic to it — zero downtime.
///
/// Thread-safety: <c>_isReady</c> is <c>volatile</c> — a single write from the startup
/// thread is immediately visible to all reader threads without additional locking.
/// </summary>
public sealed class ReadinessCheck : IHealthCheck
{
    private volatile bool _isReady;
    private readonly ILogger<ReadinessCheck> _logger;

    public ReadinessCheck(ILogger<ReadinessCheck> logger)
        => _logger = logger;

    /// <summary>
    /// Marks the application as ready to accept traffic.
    /// Called once from the <c>ApplicationStarted</c> lifetime event in <c>Program.cs</c>.
    /// </summary>
    public void MarkReady()
    {
        _isReady = true;
        _logger.LogInformation(
            "READINESS_STATE_CHANGED: Application is ready to accept traffic at {Timestamp}",
            DateTimeOffset.UtcNow.ToString("o"));
    }

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        return _isReady
            ? Task.FromResult(HealthCheckResult.Healthy("Application is ready to accept traffic"))
            : Task.FromResult(HealthCheckResult.Unhealthy("Application is still starting up"));
    }
}
