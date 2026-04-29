using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace UPACIP.Api.HealthChecks;

/// <summary>
/// Extension methods that register and map all health check services for the UPACIP API
/// (US_099, AC-1, AC-2, AC-3, AC-4, NFR-020, edge cases 1 and 2).
///
/// Three endpoints are produced:
/// <list type="bullet">
///   <item>
///     <c>GET /health</c> — full dependency-level check. Runs all non-readiness checks
///     (database, Redis, AI gateway, TLS certificate, audit queue) and returns detailed JSON.
///     Overall status reflects the worst individual status. Publicly accessible (no auth).
///   </item>
///   <item>
///     <c>GET /ready</c> — readiness probe scoped to <c>"ready"</c>-tagged checks only
///     (database, Redis, and the <see cref="ReadinessCheck"/> startup gate).
///     Returns HTTP 503 during startup or when any critical dependency is
///     <see cref="HealthStatus.Unhealthy"/>. Used by IIS Application Initialization and
///     load balancers (AC-2, edge case 2).
///   </item>
/// </list>
///
/// Per-check timeout strategy (edge case 1):
/// Each custom check is registered with a 400 ms individual timeout.  When a check exceeds
/// its budget, the health-check framework cancels it and records it as
/// <see cref="HealthStatus.Unhealthy"/>.  This leaves 100 ms headroom for response
/// serialization and network overhead, keeping the total response under 500 ms.
///
/// Rolling deployment (edge case 2):
/// The <see cref="ReadinessCheck"/> returns Unhealthy until
/// <see cref="ReadinessCheck.MarkReady"/> is called from the
/// <c>IHostApplicationLifetime.ApplicationStarted</c> event.  Load balancers poll
/// <c>/ready</c> — the new instance returns 503 until fully initialised, ensuring the old
/// instance continues serving traffic throughout the deployment.
/// </summary>
public static class HealthCheckConfiguration
{
    private static readonly string[] DbTags         = { "db", "critical", "ready" };
    private static readonly string[] CacheTags      = { "cache", "non-critical", "ready" };
    private static readonly string[] AiTags         = { "ai", "non-critical" };
    private static readonly string[] ReadinessTags  = { "ready" };
    private static readonly string[] AuditTags      = { "audit" };

    /// <summary>
    /// Registers the three custom dependency checks (database, Redis, AI gateway) into the
    /// existing <see cref="IHealthChecksBuilder"/> created in <c>Program.cs</c>.
    /// </summary>
    public static IHealthChecksBuilder AddDependencyChecks(this IHealthChecksBuilder builder)
    {
        return builder
            .AddCheck<DatabaseHealthCheck>(
                name: "postgresql",
                failureStatus: HealthStatus.Unhealthy,
                tags: DbTags,
                timeout: TimeSpan.FromMilliseconds(400))
            .AddCheck<RedisHealthCheck>(
                name: "redis",
                failureStatus: HealthStatus.Degraded,
                tags: CacheTags,
                timeout: TimeSpan.FromMilliseconds(400))
            .AddCheck<AiGatewayHealthCheck>(
                name: "ai-gateway",
                failureStatus: HealthStatus.Degraded,
                tags: AiTags,
                timeout: TimeSpan.FromMilliseconds(400));
    }

    /// <summary>
    /// Registers <see cref="ReadinessCheck"/> as a singleton and adds it to the health-check
    /// pipeline tagged <c>"ready"</c>.  Must be called after <c>AddHealthChecks()</c> in
    /// <c>Program.cs</c> so the builder returned from <c>AddHealthChecks</c> is reused (AC-2).
    /// </summary>
    public static IServiceCollection AddReadinessCheck(this IServiceCollection services)
    {
        // Singleton so Program.cs can resolve the same instance to call MarkReady().
        services.AddSingleton<ReadinessCheck>();

        services.AddHealthChecks()
            .AddCheck<ReadinessCheck>(
                name: "readiness",
                failureStatus: HealthStatus.Unhealthy,
                tags: ReadinessTags,
                timeout: TimeSpan.FromMilliseconds(200));

        return services;
    }

    /// <summary>
    /// Registers <see cref="HealthStateMonitorService"/> as a hosted background service (AC-4).
    /// </summary>
    public static IServiceCollection AddHealthStateMonitoring(this IServiceCollection services)
    {
        services.AddHostedService<HealthStateMonitorService>();
        return services;
    }

    /// <summary>
    /// Maps the <c>/health</c> and <c>/ready</c> health check endpoints.
    ///
    /// <c>/health</c> evaluates all dependency checks (excludes the startup readiness gate).
    /// <c>/ready</c> evaluates only <c>"ready"</c>-tagged checks including the startup gate.
    ///
    /// Both endpoints are publicly accessible (no authentication) so load balancers,
    /// Kubernetes probes, IIS Application Initialization, and deployment scripts can query
    /// them without credentials.
    /// </summary>
    public static WebApplication MapHealthCheckEndpoints(this WebApplication app)
    {
        // Full dependency report — all checks except the startup readiness gate.
        // Predicate excludes the "readiness" check by name (startup gate only; not a dependency).
        // Detailed JSON output via HealthCheckResponseWriter.
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            Predicate             = check => check.Name != "readiness",
            ResponseWriter        = HealthCheckResponseWriter.WriteAsync,
            AllowCachingResponses = false,
        }).AllowAnonymous();

        // Readiness probe — startup gate + critical dependencies tagged "ready".
        // Returns 503 during startup (AC-2) or when a critical dependency is Unhealthy.
        // Supports rolling deployments and IIS Application Initialization (edge case 2).
        var readyOptions = new HealthCheckOptions
        {
            Predicate             = check => check.Tags.Contains("ready"),
            ResponseWriter        = HealthCheckResponseWriter.WriteAsync,
            AllowCachingResponses = false,
        };
        readyOptions.ResultStatusCodes[HealthStatus.Healthy]   = StatusCodes.Status200OK;
        readyOptions.ResultStatusCodes[HealthStatus.Degraded]  = StatusCodes.Status200OK;
        readyOptions.ResultStatusCodes[HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable;

        app.MapHealthChecks("/ready", readyOptions).AllowAnonymous();

        return app;
    }
}
