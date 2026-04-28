using System.Diagnostics;

namespace UPACIP.Service.Performance;

/// <summary>
/// Contract for APM instrumentation across the UPACIP platform (US_081 task_001, AC-4).
///
/// <para>
/// Provides span-level distributed tracing via <see cref="System.Diagnostics.Activity"/>
/// and thread-safe in-memory latency histogram recording for P95 SLA evaluation.
/// Compatible with OpenTelemetry exporters (if adopted in a later phase) through the
/// <c>ActivitySource</c> / <c>Meter</c> APIs.
/// </para>
/// </summary>
public interface IPerformanceTracker
{
    /// <summary>
    /// Creates and starts a root <see cref="Activity"/> span for the named operation.
    /// Sets the activity as the ambient current activity so child spans are parented correctly.
    /// </summary>
    /// <param name="operationType">
    /// Logical operation category (e.g. <c>Booking</c>, <c>DocumentParsing</c>, <c>General</c>).
    /// </param>
    /// <param name="tags">Optional key/value tags attached to the span (no PII values).</param>
    /// <returns>
    /// The started <see cref="Activity"/> (null if no listeners are registered for the source).
    /// Callers should pass the returned activity to <see cref="CompleteOperation"/> when done.
    /// </returns>
    Activity? StartOperation(string operationType, Dictionary<string, string>? tags = null);

    /// <summary>
    /// Creates and starts a child <see cref="Activity"/> span under a parent operation.
    /// Used to capture sub-operation timings (database queries, cache lookups, AI calls).
    /// </summary>
    /// <param name="spanName">
    /// Span name following the convention <c>category.action</c>
    /// (e.g. <c>db.query</c>, <c>cache.get</c>, <c>ai.inference</c>, <c>http.external</c>).
    /// </param>
    /// <param name="parentActivity">
    /// Parent activity for nesting. When null, the ambient <see cref="Activity.Current"/>
    /// is used automatically.
    /// </param>
    /// <returns>Started child activity; null if no listeners registered.</returns>
    Activity? StartSpan(string spanName, Activity? parentActivity = null);

    /// <summary>
    /// Records a completed operation's end-to-end latency into the sliding-window
    /// histogram for P95 computation.  Thread-safe; does not block the caller.
    /// </summary>
    /// <param name="operationType">Operation category (must match an SLA threshold key).</param>
    /// <param name="latencyMs">End-to-end duration in milliseconds.</param>
    void RecordLatency(string operationType, long latencyMs);

    /// <summary>
    /// Stops the activity span, records final status tag, and emits the
    /// <c>upacip.operation.duration</c> <see cref="System.Diagnostics.Metrics.Meter"/> measurement.
    /// </summary>
    /// <param name="activity">Activity to complete; no-op when null.</param>
    /// <param name="success">
    /// <see langword="true"/> when the operation completed successfully (HTTP &lt; 500).
    /// </param>
    void CompleteOperation(Activity? activity, bool success = true);

    /// <summary>
    /// Returns a snapshot of all per-operation latency samples within the current
    /// sliding window for SLA computation. Used by <see cref="ISlaMonitorService"/>.
    /// </summary>
    /// <param name="windowStart">Lower bound of the sliding window (UTC).</param>
    /// <returns>
    /// Dictionary from operation type to ordered list of <c>(timestamp, latencyMs)</c> tuples.
    /// </returns>
    IReadOnlyDictionary<string, IReadOnlyList<(DateTime Timestamp, long LatencyMs)>>
        GetSamples(DateTime windowStart);
}
