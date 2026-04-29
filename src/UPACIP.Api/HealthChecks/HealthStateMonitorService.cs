using System.Collections.Concurrent;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace UPACIP.Api.HealthChecks;

/// <summary>
/// Background service that periodically evaluates all registered health checks and logs a
/// structured event whenever a dependency transitions between health states (US_099, AC-4).
///
/// Polling interval: 60 seconds — balances monitoring responsiveness with resource usage.
/// State comparison uses a <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed on
/// dependency name to detect changes between consecutive polls.
///
/// Log levels by severity:
/// <list type="bullet">
///   <item><see cref="HealthStatus.Unhealthy"/> → <see cref="LogLevel.Critical"/> (requires immediate action)</item>
///   <item><see cref="HealthStatus.Degraded"/>  → <see cref="LogLevel.Warning"/>  (elevated risk)</item>
///   <item>Recovery to <see cref="HealthStatus.Healthy"/> → <see cref="LogLevel.Information"/></item>
/// </list>
///
/// An additional <c>"_overall"</c> synthetic entry tracks aggregate report status and
/// includes the list of affected (non-healthy) dependencies on each overall transition.
/// </summary>
public sealed class HealthStateMonitorService : BackgroundService
{
    private static readonly TimeSpan PollInterval    = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StartupDelay    = TimeSpan.FromSeconds(10);

    private readonly HealthCheckService _healthCheckService;
    private readonly ILogger<HealthStateMonitorService> _logger;
    private readonly ConcurrentDictionary<string, HealthStateRecord> _stateMap = new();

    public HealthStateMonitorService(
        HealthCheckService healthCheckService,
        ILogger<HealthStateMonitorService> logger)
    {
        _healthCheckService = healthCheckService;
        _logger             = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Brief startup delay prevents false state-change events during application init.
        await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false);

        _logger.LogInformation("HEALTH_MONITOR_STARTED: State change monitoring active (interval=60s)");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndLogTransitionsAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "HEALTH_MONITOR_ERROR: Failed to evaluate health state at {Timestamp}",
                    DateTimeOffset.UtcNow.ToString("o"));
            }

            await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
        }

        _logger.LogInformation("HEALTH_MONITOR_STOPPED: State change monitoring stopped");
    }

    private async Task CheckAndLogTransitionsAsync(CancellationToken ct)
    {
        var report = await _healthCheckService.CheckHealthAsync(ct).ConfigureAwait(false);

        foreach (var (name, entry) in report.Entries)
        {
            var currentStatus = entry.Status;

            var record = _stateMap.GetOrAdd(name, _ => new HealthStateRecord
            {
                DependencyName  = name,
                PreviousStatus  = currentStatus,
                CurrentStatus   = currentStatus,
                LastChangedUtc  = DateTime.UtcNow,
                Description     = entry.Description,
            });

            if (record.CurrentStatus != currentStatus)
            {
                var previous = record.CurrentStatus;
                record.PreviousStatus = previous;
                record.CurrentStatus  = currentStatus;
                record.LastChangedUtc = DateTime.UtcNow;
                record.Description    = entry.Description;

                LogDependencyTransition(name, previous, currentStatus, entry.Description);
            }
        }

        // Aggregate (overall) status transition.
        var overallRecord = _stateMap.GetOrAdd("_overall", _ => new HealthStateRecord
        {
            DependencyName  = "_overall",
            PreviousStatus  = report.Status,
            CurrentStatus   = report.Status,
            LastChangedUtc  = DateTime.UtcNow,
        });

        if (overallRecord.CurrentStatus != report.Status)
        {
            var previousOverall = overallRecord.CurrentStatus;
            overallRecord.PreviousStatus = previousOverall;
            overallRecord.CurrentStatus  = report.Status;
            overallRecord.LastChangedUtc = DateTime.UtcNow;

            var affected = report.Entries
                .Where(e => e.Value.Status != HealthStatus.Healthy)
                .Select(e => e.Key)
                .ToList();

            _logger.LogWarning(
                "HEALTH_STATE_CHANGED: Overall {PreviousStatus} → {CurrentStatus}, " +
                "AffectedDependencies=[{AffectedDependencies}], Timestamp={Timestamp}",
                previousOverall,
                report.Status,
                string.Join(", ", affected),
                DateTime.UtcNow.ToString("o"));
        }
    }

    private void LogDependencyTransition(
        string name,
        HealthStatus previous,
        HealthStatus current,
        string? description)
    {
        var level = current switch
        {
            HealthStatus.Unhealthy                                        => LogLevel.Critical,
            HealthStatus.Degraded                                         => LogLevel.Warning,
            HealthStatus.Healthy when previous != HealthStatus.Healthy    => LogLevel.Information,
            _                                                             => LogLevel.Debug,
        };

        _logger.Log(
            level,
            "DEPENDENCY_STATE_CHANGED: {DependencyName} {PreviousStatus} → {CurrentStatus}, " +
            "Description={Description}, Timestamp={Timestamp}",
            name,
            previous,
            current,
            description,
            DateTime.UtcNow.ToString("o"));
    }
}
