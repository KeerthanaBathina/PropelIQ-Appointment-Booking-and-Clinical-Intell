using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.Service.Monitoring.Models;
using UPACIP.Service.Performance;

namespace UPACIP.Service.Monitoring;

/// <summary>
/// BackgroundService that drives the uptime monitoring loop every 30 seconds (US_083 task_001,
/// AC-1, AC-3, AC-4, NFR-019).
///
/// <para>
/// Each probe cycle performs the following steps in order:
/// <list type="number">
///   <item>Check whether the current UTC time falls inside a configured maintenance window.</item>
///   <item>Evaluate dependency health via <see cref="HealthCheckService.CheckHealthAsync"/>.</item>
///   <item>Record a <see cref="UPACIP.DataAccess.Entities.UptimeSnapshot"/> via <see cref="IUptimeTracker"/>.</item>
///   <item>Evaluate health-state transitions and emit outage/recovery alerts via <see cref="IOutageAlertService"/>.</item>
///   <item>Check the error rate threshold and emit an alert if exceeded.</item>
///   <item>Emit the current uptime percentage as a metric via <see cref="IPerformanceTracker"/>.</item>
///   <item>Every 100th cycle — prune snapshots older than 90 days.</item>
/// </list>
/// </para>
///
/// <para>
/// The service is registered as a <see cref="IHostedService"/>.  Scoped services
/// (<see cref="IUptimeTracker"/>) are resolved via a fresh <see cref="IServiceScope"/>
/// per cycle.  <see cref="IOutageAlertService"/> and <see cref="IErrorRateMonitor"/> are
/// injected directly as singletons.
/// </para>
/// </summary>
public sealed class UptimeMonitoringService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOutageAlertService _outageAlertService;
    private readonly IErrorRateMonitor _errorRateMonitor;
    private readonly IHealthStatusProvider _healthStatusProvider;
    private readonly IPerformanceTracker _performanceTracker;
    private readonly IDegradationModeManager _degradationManager;
    private readonly IOptions<MonitoringOptions> _options;
    private readonly ILogger<UptimeMonitoringService> _logger;

    private int _cycleCount;

    public UptimeMonitoringService(
        IServiceScopeFactory scopeFactory,
        IOutageAlertService outageAlertService,
        IErrorRateMonitor errorRateMonitor,
        IHealthStatusProvider healthStatusProvider,
        IPerformanceTracker performanceTracker,
        IDegradationModeManager degradationManager,
        IOptions<MonitoringOptions> options,
        ILogger<UptimeMonitoringService> logger)
    {
        _scopeFactory         = scopeFactory;
        _outageAlertService   = outageAlertService;
        _errorRateMonitor     = errorRateMonitor;
        _healthStatusProvider = healthStatusProvider;
        _performanceTracker   = performanceTracker;
        _degradationManager   = degradationManager;
        _options              = options;
        _logger               = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(
            Math.Max(5, _options.Value.ProbeIntervalSeconds));

        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await RunProbeCycleAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Uptime monitoring probe cycle failed.");
            }

            _cycleCount++;
        }
    }

    // ── Private probe cycle ───────────────────────────────────────────────────

    private async Task RunProbeCycleAsync(CancellationToken ct)
    {
        var opts = _options.Value;
        var utcNow = DateTime.UtcNow;

        // (a) Maintenance window check.
        var isMaintenance = opts.MaintenanceWindows.Any(w => w.IsActive(utcNow));

        // (b) Evaluate health via IHealthStatusProvider (delegates to HealthCheckService in the API layer).
        var (isHealthy, statuses) = await _healthStatusProvider.EvaluateAsync(ct);

        using var scope = _scopeFactory.CreateScope();
        var uptimeTracker = scope.ServiceProvider.GetRequiredService<IUptimeTracker>();

        // (c) Record snapshot.
        await uptimeTracker.RecordSnapshotAsync(isHealthy, statuses, isMaintenance, ct);

        // (d) Evaluate health transitions and emit alerts.
        await _outageAlertService.EvaluateHealthTransitionAsync(statuses, isMaintenance, ct);

        // (d2) Update degradation manager based on dependency health transitions.
        UpdateDegradationState(statuses);

        // (e) Error rate threshold check.
        if (_errorRateMonitor.IsThresholdExceeded())
        {
            var rate       = _errorRateMonitor.GetCurrentErrorRate() * 100.0;
            var byCategory = _errorRateMonitor.GetErrorRateByCategory();
            var topCategory = byCategory
                .OrderByDescending(kv => kv.Value.Errors)
                .Select(kv => kv.Key)
                .FirstOrDefault() ?? "unknown";

            _logger.LogWarning(
                "ERROR_RATE_ALERT: Rate={ErrorRate:F3}%, Threshold={Threshold}%, " +
                "TopCategory={TopCategory}, Window={WindowSeconds}s",
                rate, opts.ErrorRateThresholdPercent, topCategory, opts.ErrorRateWindowSeconds);
        }

        // (f) Emit uptime percentage metric.
        var uptimePercent = await uptimeTracker.GetUptimePercentageAsync(opts.UptimeWindowDays, ct);
        _performanceTracker.RecordLatency("system.uptime_percent", (long)uptimePercent);

        // (g) Every 100th cycle: prune snapshots older than 90 days.
        if (_cycleCount % 100 == 0)
        {
            await uptimeTracker.PruneOldSnapshotsAsync(retentionDays: 90, ct);
        }
    }

    // ── Degradation state update ──────────────────────────────────────────────

    /// <summary>
    /// Maps dependency health check statuses to <see cref="DependencyCategory"/> values and
    /// drives <see cref="IDegradationModeManager"/> activate/deactivate calls so the
    /// graceful-degradation middleware always reflects the latest probe results.
    /// </summary>
    private void UpdateDegradationState(Dictionary<string, string> statuses)
    {
        foreach (var (name, status) in statuses)
        {
            var category = MapStatusKeyToCategory(name);
            if (category is null) continue;

            var isHealthy = string.Equals(status, "Healthy", StringComparison.OrdinalIgnoreCase);

            if (isHealthy)
                _degradationManager.DeactivateDegradation(category.Value);
            else
                _degradationManager.ActivateDegradation(category.Value);
        }
    }

    private static DependencyCategory? MapStatusKeyToCategory(string key) => key.ToLowerInvariant() switch
    {
        "database"    => DependencyCategory.Database,
        "redis"       => DependencyCategory.Redis,
        "ai_openai"   => DependencyCategory.AiProviders,
        "ai_anthropic"=> DependencyCategory.AiProviders,
        "openai"      => DependencyCategory.AiProviders,
        "anthropic"   => DependencyCategory.AiProviders,
        _             => null,
    };
}
