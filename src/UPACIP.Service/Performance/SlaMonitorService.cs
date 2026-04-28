using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.Service.Performance.Models;

namespace UPACIP.Service.Performance;

/// <summary>
/// <see cref="ISlaMonitorService"/> implementation with P95 nearest-rank computation,
/// sliding-window filtering, trend analysis, and alert cooldown enforcement
/// (US_081 task_001, AC-4).
///
/// <para>
/// <b>Percentile algorithm:</b> Nearest-rank method —
/// index = ceil(percentage × count) − 1 (0-based), applied to the sorted sample array.
/// </para>
///
/// <para>
/// <b>Minimum sample requirement:</b> 10 samples per operation per window.
/// Operations with fewer samples are skipped to avoid misleading P95 values.
/// </para>
///
/// <para>
/// <b>Trend detection:</b> Compares current P95 to the previous evaluation's P95 for
/// the same operation. A change of more than ±5% is classified as Improving/Degrading;
/// within ±5% is Stable.
/// </para>
///
/// <para>Singleton lifetime — thread-safe in-memory state.</para>
/// </summary>
public sealed class SlaMonitorService : ISlaMonitorService
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const int    MinSampleCount   = 10;
    private const double TrendNoiseMargin = 0.05; // ±5% treated as Stable.

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly IPerformanceTracker               _tracker;
    private readonly PerformanceOptions                _options;
    private readonly ILogger<SlaMonitorService>        _logger;

    // Previous evaluation P95 per operation type — for trend detection.
    private readonly ConcurrentDictionary<string, double>   _previousP95  = new(StringComparer.OrdinalIgnoreCase);

    // Last alert emission time per operation type — for cooldown enforcement.
    private readonly ConcurrentDictionary<string, DateTime> _lastAlertAt  = new(StringComparer.OrdinalIgnoreCase);

    // ── Constructor ───────────────────────────────────────────────────────────

    public SlaMonitorService(
        IPerformanceTracker         tracker,
        IOptions<PerformanceOptions> options,
        ILogger<SlaMonitorService>  logger)
    {
        _tracker = tracker;
        _options = options.Value;
        _logger  = logger;
    }

    // ── ISlaMonitorService ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Task<IReadOnlyList<OperationMetric>> GetCurrentMetricsAsync(
        CancellationToken ct = default)
    {
        var windowStart = DateTime.UtcNow.AddMinutes(-_options.SlidingWindowMinutes);
        var windowEnd   = DateTime.UtcNow;

        var samples = _tracker.GetSamples(windowStart);
        var metrics = new List<OperationMetric>(samples.Count);

        foreach (var (operationType, rawSamples) in samples)
        {
            if (rawSamples.Count < MinSampleCount)
                continue;

            // Extract sorted latency values (ascending) for percentile computation.
            var sortedMs = rawSamples
                .Select(s => s.LatencyMs)
                .OrderBy(ms => ms)
                .ToArray();

            var p50 = NearestRank(sortedMs, 0.50);
            var p95 = NearestRank(sortedMs, 0.95);
            var p99 = NearestRank(sortedMs, 0.99);

            var isWithinSla = !_options.SlaThresholdsMs.TryGetValue(operationType, out var threshold)
                              || p95 <= threshold;

            metrics.Add(new OperationMetric
            {
                OperationType = operationType,
                P50Ms         = p50,
                P95Ms         = p95,
                P99Ms         = p99,
                SampleCount   = sortedMs.Length,
                WindowStart   = windowStart,
                WindowEnd     = windowEnd,
                IsWithinSla   = isWithinSla,
            });
        }

        return Task.FromResult<IReadOnlyList<OperationMetric>>(metrics);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SlaAlert>> EvaluateSlaComplianceAsync(
        CancellationToken ct = default)
    {
        var metrics = await GetCurrentMetricsAsync(ct);
        var alerts  = new List<SlaAlert>();

        foreach (var metric in metrics)
        {
            // Only generate alerts for operations with configured SLA thresholds.
            if (!_options.SlaThresholdsMs.TryGetValue(metric.OperationType, out var threshold))
                continue;

            if (metric.P95Ms <= threshold)
            {
                // Within SLA — update previous P95 for next cycle's trend.
                _previousP95[metric.OperationType] = metric.P95Ms;
                continue;
            }

            // ── Alert cooldown check ──────────────────────────────────────────
            if (_lastAlertAt.TryGetValue(metric.OperationType, out var lastAlert)
                && (DateTime.UtcNow - lastAlert).TotalMinutes < _options.AlertCooldownMinutes)
            {
                // Suppress duplicate alert within cooldown window.
                _previousP95[metric.OperationType] = metric.P95Ms;
                continue;
            }

            // ── Trend computation ─────────────────────────────────────────────
            var trend = TrendDirection.Stable;
            if (_previousP95.TryGetValue(metric.OperationType, out var prevP95) && prevP95 > 0)
            {
                var delta = (metric.P95Ms - prevP95) / prevP95;
                if (delta > TrendNoiseMargin)
                    trend = TrendDirection.Degrading;
                else if (delta < -TrendNoiseMargin)
                    trend = TrendDirection.Improving;
            }

            var message = $"{metric.OperationType} P95 latency {metric.P95Ms:F0}ms " +
                          $"exceeds {threshold}ms threshold ({trend})";

            var alert = new SlaAlert
            {
                OperationType = metric.OperationType,
                CurrentP95Ms  = metric.P95Ms,
                ThresholdMs   = threshold,
                Trend         = trend,
                Timestamp     = DateTime.UtcNow,
                Message       = message,
            };

            alerts.Add(alert);

            // Update state for next evaluation.
            _previousP95[metric.OperationType] = metric.P95Ms;
            _lastAlertAt[metric.OperationType] = DateTime.UtcNow;

            _logger.LogWarning(
                "SLA breach: {OperationType} P95={P95Ms:F0}ms exceeds {ThresholdMs}ms ({Trend}). " +
                "SampleCount={SampleCount}",
                metric.OperationType, metric.P95Ms, threshold, trend, metric.SampleCount);
        }

        return alerts;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Nearest-rank percentile: index = ceil(p × n) − 1 (0-based).
    /// <paramref name="sorted"/> must be sorted ascending.
    /// </summary>
    private static double NearestRank(long[] sorted, double percentile)
    {
        if (sorted.Length == 0) return 0;

        var rank  = (int)Math.Ceiling(percentile * sorted.Length);
        var index = Math.Clamp(rank - 1, 0, sorted.Length - 1);
        return sorted[index];
    }
}
