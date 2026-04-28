using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.Service.Performance.Models;

namespace UPACIP.Service.Performance;

/// <summary>
/// Background service that periodically evaluates SLA compliance and emits
/// structured performance alerts via Serilog (US_081 task_001, AC-4).
///
/// <para>
/// <b>Tick behaviour:</b>
/// <list type="number">
///   <item>Calls <see cref="ISlaMonitorService.EvaluateSlaComplianceAsync"/> to detect
///     SLA breaches and emit per-breach warning-level log entries.</item>
///   <item>Calls <see cref="ISlaMonitorService.GetCurrentMetricsAsync"/> to emit a
///     summary Information-level log with the current P95 for each tracked operation.</item>
/// </list>
/// </para>
///
/// <para>
/// Exceptions during evaluation are logged and do not crash the host.
/// The next tick proceeds normally after any failure.
/// </para>
///
/// <para>Uses <see cref="PeriodicTimer"/> for accurate interval scheduling.</para>
/// </summary>
public sealed class PerformanceMonitoringService : BackgroundService
{
    private readonly ISlaMonitorService                     _slaMonitor;
    private readonly PerformanceOptions                     _options;
    private readonly ILogger<PerformanceMonitoringService>  _logger;

    public PerformanceMonitoringService(
        ISlaMonitorService                    slaMonitor,
        IOptions<PerformanceOptions>          options,
        ILogger<PerformanceMonitoringService> logger)
    {
        _slaMonitor = slaMonitor;
        _options    = options.Value;
        _logger     = logger;
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.EvaluationIntervalSeconds);

        _logger.LogInformation(
            "PerformanceMonitoringService: started. EvaluationInterval={Interval}s " +
            "SlidingWindow={Window}min AlertCooldown={Cooldown}min",
            _options.EvaluationIntervalSeconds,
            _options.SlidingWindowMinutes,
            _options.AlertCooldownMinutes);

        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await EvaluateOnceAsync(stoppingToken);
        }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task EvaluateOnceAsync(CancellationToken ct)
    {
        try
        {
            // ── SLA breach alerts ─────────────────────────────────────────────
            // Individual breach log entries are emitted inside EvaluateSlaComplianceAsync.
            var alerts = await _slaMonitor.EvaluateSlaComplianceAsync(ct);

            if (alerts.Count > 0)
            {
                _logger.LogWarning(
                    "PerformanceMonitoringService: {AlertCount} SLA breach(es) detected in this evaluation cycle.",
                    alerts.Count);
            }

            // ── P95 summary log ───────────────────────────────────────────────
            var metrics = await _slaMonitor.GetCurrentMetricsAsync(ct);

            if (metrics.Count == 0)
            {
                _logger.LogDebug("PerformanceMonitoringService: no metrics collected yet (waiting for samples).");
                return;
            }

            // Build a structured summary with known operation types surfaced as named properties.
            var bookingP95  = FindP95(metrics, "Booking");
            var parsingP95  = FindP95(metrics, "DocumentParsing");
            var codingP95   = FindP95(metrics, "MedicalCoding");

            _logger.LogInformation(
                "Performance summary: Booking P95={BookingP95}ms, " +
                "Parsing P95={ParsingP95}ms, Coding P95={CodingP95}ms. " +
                "Total tracked operations={OperationCount}",
                bookingP95.HasValue  ? $"{bookingP95.Value:F0}" : "n/a",
                parsingP95.HasValue  ? $"{parsingP95.Value:F0}" : "n/a",
                codingP95.HasValue   ? $"{codingP95.Value:F0}"  : "n/a",
                metrics.Count);

            // Log each tracked operation's full percentile profile at Debug level.
            foreach (var m in metrics)
            {
                _logger.LogDebug(
                    "PerformanceMetric: {OperationType} P50={P50Ms:F0}ms P95={P95Ms:F0}ms " +
                    "P99={P99Ms:F0}ms Samples={SampleCount} WithinSla={IsWithinSla}",
                    m.OperationType, m.P50Ms, m.P95Ms, m.P99Ms, m.SampleCount, m.IsWithinSla);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown — let the loop exit.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "PerformanceMonitoringService: evaluation failed. Will retry on next tick.");
        }
    }

    private static double? FindP95(IReadOnlyList<OperationMetric> metrics, string operationType)
    {
        foreach (var m in metrics)
        {
            if (string.Equals(m.OperationType, operationType, StringComparison.OrdinalIgnoreCase))
                return m.P95Ms;
        }
        return null;
    }
}
