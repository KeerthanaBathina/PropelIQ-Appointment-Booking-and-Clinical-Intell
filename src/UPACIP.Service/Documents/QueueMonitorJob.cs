using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UPACIP.Service.Documents;

/// <summary>
/// Periodic background job that invokes <see cref="IQueueMonitorService.CheckQueueHealthAsync"/>
/// on the configured interval to detect stale items and escalate on high queue depth
/// (US_071 TASK_003, AC-4).
///
/// <para>
/// Scheduling: <see cref="PeriodicTimer"/> fires every
/// <see cref="AiQueueOptions.MonitorIntervalSeconds"/> (default: 60 s).
/// </para>
///
/// <para>
/// Resilience: exceptions from a single health-check run are caught and logged as errors
/// so the monitoring loop continues uninterrupted on the next tick.
/// </para>
///
/// <para>
/// Lifetime: Singleton <see cref="BackgroundService"/>. Both <see cref="IQueueMonitorService"/>
/// and <see cref="IDocumentParsingQueue"/> are singleton-safe, so no
/// <see cref="Microsoft.Extensions.DependencyInjection.IServiceScopeFactory"/> scope is required.
/// </para>
/// </summary>
public sealed class QueueMonitorJob : BackgroundService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly IQueueMonitorService         _monitor;
    private readonly AiQueueOptions               _options;
    private readonly ILogger<QueueMonitorJob>     _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public QueueMonitorJob(
        IQueueMonitorService     monitor,
        IOptions<AiQueueOptions> options,
        ILogger<QueueMonitorJob> logger)
    {
        _monitor = monitor;
        _options = options.Value;
        _logger  = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BackgroundService.ExecuteAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "QueueMonitorJob: started. IntervalSeconds={Interval} " +
            "StaleWarningMinutes={StaleMinutes} EscalationThreshold={Threshold}.",
            _options.MonitorIntervalSeconds,
            _options.StaleWarningMinutes,
            _options.EscalationDepthThreshold);

        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(_options.MonitorIntervalSeconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await _monitor.CheckQueueHealthAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown — expected; exit loop.
                break;
            }
            catch (Exception ex)
            {
                // Log and continue so a single failed health-check does not stop monitoring.
                _logger.LogError(ex,
                    "QueueMonitorJob: unhandled exception during health check. " +
                    "Will retry on next tick ({Interval}s).",
                    _options.MonitorIntervalSeconds);
            }
        }

        _logger.LogInformation("QueueMonitorJob: stopped.");
    }
}
