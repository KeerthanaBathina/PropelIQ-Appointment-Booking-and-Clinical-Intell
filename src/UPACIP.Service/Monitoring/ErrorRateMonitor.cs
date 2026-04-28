using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using UPACIP.Service.Monitoring.Models;

namespace UPACIP.Service.Monitoring;

/// <summary>
/// Sliding-window HTTP error rate monitor backed by a thread-safe <see cref="ConcurrentQueue{T}"/>
/// (US_083 task_001, AC-4, NFR-031).
///
/// <para>
/// Each request outcome is enqueued as a <c>(Timestamp, IsError, Category)</c> tuple.
/// Stale entries outside the configured window are evicted lazily on each read or write
/// to bound memory consumption.  At 100 RPS the 5-minute window holds ~30 000 entries;
/// the tuple struct is 24 bytes each, giving ~720 KB steady-state overhead.
/// </para>
///
/// <para>
/// <b>Registered as Singleton</b> — the queue must accumulate entries across all requests.
/// </para>
/// </summary>
public sealed class ErrorRateMonitor : IErrorRateMonitor
{
    private readonly IOptions<MonitoringOptions> _options;

    // (Timestamp, IsError, Category) — struct to minimise allocations.
    private readonly ConcurrentQueue<RequestEntry> _entries = new();

    public ErrorRateMonitor(IOptions<MonitoringOptions> options)
    {
        _options = options;
    }

    /// <inheritdoc />
    public void RecordRequestOutcome(bool isError, string category)
    {
        _entries.Enqueue(new RequestEntry(DateTime.UtcNow, isError, category));
        EvictOldEntries();
    }

    /// <inheritdoc />
    public double GetCurrentErrorRate()
    {
        EvictOldEntries();
        var snapshot = _entries.ToArray();
        if (snapshot.Length == 0) return 0.0;
        var errors = snapshot.Count(e => e.IsError);
        return (double)errors / snapshot.Length;
    }

    /// <inheritdoc />
    public bool IsThresholdExceeded()
    {
        var threshold = _options.Value.ErrorRateThresholdPercent / 100.0;
        return GetCurrentErrorRate() > threshold;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, (int Errors, int Total)> GetErrorRateByCategory()
    {
        EvictOldEntries();
        var snapshot = _entries.ToArray();
        var result   = new Dictionary<string, (int Errors, int Total)>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in snapshot)
        {
            var cat = entry.Category;
            result.TryGetValue(cat, out var current);
            result[cat] = (current.Errors + (entry.IsError ? 1 : 0), current.Total + 1);
        }

        return result;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private void EvictOldEntries()
    {
        var windowSeconds = _options.Value.ErrorRateWindowSeconds;
        var cutoff = DateTime.UtcNow.AddSeconds(-windowSeconds);

        // Dequeue from the front while entries are older than the window.
        while (_entries.TryPeek(out var oldest) && oldest.Timestamp < cutoff)
            _entries.TryDequeue(out _);
    }

    // Value-type entry to avoid per-item heap allocations.
    private readonly record struct RequestEntry(DateTime Timestamp, bool IsError, string Category);
}
