using Microsoft.Extensions.Diagnostics.HealthChecks;
using UPACIP.Service.Monitoring;

namespace UPACIP.Api.HealthChecks;

/// <summary>
/// API-layer adapter that delegates to ASP.NET Core's built-in <see cref="HealthCheckService"/>
/// to provide in-process dependency health evaluation for <see cref="UPACIP.Service.Monitoring.UptimeMonitoringService"/>
/// (US_083 task_001, AC-1).
///
/// <para>
/// Registered as <see cref="IHealthStatusProvider"/> (Singleton) in <c>Program.cs</c>.
/// Using the built-in service avoids HTTP overhead and eliminates the self-referencing HTTP call
/// that external probing would require.
/// </para>
/// </summary>
public sealed class AspNetHealthStatusProvider : IHealthStatusProvider
{
    private readonly HealthCheckService _healthCheckService;

    public AspNetHealthStatusProvider(HealthCheckService healthCheckService)
    {
        _healthCheckService = healthCheckService;
    }

    /// <inheritdoc />
    public async Task<(bool IsHealthy, Dictionary<string, string> Statuses)> EvaluateAsync(
        CancellationToken ct = default)
    {
        var report = await _healthCheckService.CheckHealthAsync(ct);

        var statuses = report.Entries.ToDictionary(
            kv => kv.Key,
            kv => kv.Value.Status.ToString(),
            StringComparer.OrdinalIgnoreCase);

        return (report.Status == HealthStatus.Healthy, statuses);
    }
}
