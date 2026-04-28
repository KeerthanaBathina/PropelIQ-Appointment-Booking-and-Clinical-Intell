using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Options;
using UPACIP.Service.Performance.Models;

namespace UPACIP.Service.Performance;

/// <summary>
/// <see cref="IPerformanceTracker"/> implementation using <see cref="ActivitySource"/>
/// for distributed tracing and <see cref="Meter"/> for metrics (US_081 task_001, AC-4).
///
/// <para>
/// <b>Activity source name:</b> <c>"UPACIP.Performance"</c>.
/// OpenTelemetry exporters can subscribe to this name to receive span data.
/// </para>
///
/// <para>
/// <b>Histogram buffer:</b> Thread-safe <see cref="ConcurrentQueue{T}"/> per operation type
/// with a maximum of 10,000 samples. Expired samples (older than the sliding window) are
/// evicted lazily during <see cref="GetSamples"/> reads.
/// </para>
///
/// <para>
/// <b>Meter:</b> Named <c>"UPACIP.Performance"</c>; exposes a
/// <c>upacip.operation.duration</c> histogram instrument for each operation type.
/// </para>
///
/// <para>Singleton lifetime — thread-safe buffer, static ActivitySource.</para>
/// </summary>
public sealed class PerformanceTracker : IPerformanceTracker, IDisposable
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const string ActivitySourceName       = "UPACIP.Performance";
    private const string MeterName                = "UPACIP.Performance";
    private const string DurationInstrumentName   = "upacip.operation.duration";
    private const int    MaxSamplesPerOperation   = 10_000;

    // ── Static shared ActivitySource ─────────────────────────────────────────

    /// <summary>
    /// Shared <see cref="System.Diagnostics.ActivitySource"/> for UPACIP.
    /// Static so callers inside <c>UPACIP.Service</c> can start child spans
    /// without injecting this class.
    /// </summary>
    public static readonly ActivitySource Source = new(ActivitySourceName, "1.0.0");

    // ── Fields ────────────────────────────────────────────────────────────────

    private readonly PerformanceOptions                                                    _options;
    private readonly Meter                                                                 _meter;
    private readonly Histogram<long>                                                       _durationHistogram;

    // Per-operation circular buffer: operation type → queue of (timestamp, latencyMs).
    private readonly ConcurrentDictionary<string, ConcurrentQueue<(DateTime Ts, long Ms)>> _buffers = new(StringComparer.OrdinalIgnoreCase);

    // ── Constructor ───────────────────────────────────────────────────────────

    public PerformanceTracker(IOptions<PerformanceOptions> options)
    {
        _options = options.Value;
        _meter   = new Meter(MeterName, "1.0.0");

        // Single shared histogram — operation type is attached as a tag.
        _durationHistogram = _meter.CreateHistogram<long>(
            DurationInstrumentName,
            unit:        "ms",
            description: "End-to-end latency per operation type (ms).");
    }

    // ── IPerformanceTracker ───────────────────────────────────────────────────

    /// <inheritdoc/>
    public Activity? StartOperation(string operationType, Dictionary<string, string>? tags = null)
    {
        if (!_options.EnableSpanTracing) return null;

        var activity = Source.StartActivity(
            operationType,
            ActivityKind.Internal);

        if (activity is null) return null;

        activity.SetTag("operation.type", operationType);

        if (tags is not null)
        {
            foreach (var (k, v) in tags)
                activity.SetTag(k, v);
        }

        return activity;
    }

    /// <inheritdoc/>
    public Activity? StartSpan(string spanName, Activity? parentActivity = null)
    {
        if (!_options.EnableSpanTracing) return null;

        // When parentActivity is explicitly provided, set it as the parent context.
        ActivityContext parentContext = parentActivity is not null
            ? parentActivity.Context
            : Activity.Current?.Context ?? default;

        var span = Source.StartActivity(
            spanName,
            ActivityKind.Internal,
            parentContext);

        return span;
    }

    /// <inheritdoc/>
    public void RecordLatency(string operationType, long latencyMs)
    {
        var queue = _buffers.GetOrAdd(
            operationType,
            _ => new ConcurrentQueue<(DateTime, long)>());

        queue.Enqueue((DateTime.UtcNow, latencyMs));

        // Bounded eviction: drop oldest entries when buffer exceeds capacity.
        // Uses a simple threshold check — exact trim is not required for histograms.
        while (queue.Count > MaxSamplesPerOperation && queue.TryDequeue(out _)) { }
    }

    /// <inheritdoc/>
    public void CompleteOperation(Activity? activity, bool success = true)
    {
        if (activity is null) return;

        activity.SetTag("operation.success", success);

        if (activity.Duration > TimeSpan.Zero)
        {
            var durationMs = (long)activity.Duration.TotalMilliseconds;
            var operationType = activity.OperationName;

            // Record to the shared Meter histogram with operation type tag.
            _durationHistogram.Record(
                durationMs,
                new KeyValuePair<string, object?>("operation.type", operationType),
                new KeyValuePair<string, object?>("operation.success", success));
        }

        activity.SetStatus(success ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        activity.Stop();
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, IReadOnlyList<(DateTime Timestamp, long LatencyMs)>>
        GetSamples(DateTime windowStart)
    {
        var result = new Dictionary<string, IReadOnlyList<(DateTime, long)>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (operationType, queue) in _buffers)
        {
            // Snapshot the queue and filter to the requested window.
            var filtered = queue
                .Where(s => s.Ts >= windowStart)
                .Select(s => (s.Ts, s.Ms))
                .ToList();

            if (filtered.Count > 0)
                result[operationType] = filtered;
        }

        return result;
    }

    // ── IDisposable ───────────────────────────────────────────────────────────

    public void Dispose()
    {
        _meter.Dispose();
    }
}
