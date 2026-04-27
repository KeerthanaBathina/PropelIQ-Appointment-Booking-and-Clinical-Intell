using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace UPACIP.Api.Features.AIGateway.Services;

/// <summary>
/// Thread-safe singleton that tracks per-provider health metrics and estimated cost
/// for all AI Gateway requests (US_067 TASK_002, AIR-O09).
///
/// Metrics tracked per provider:
/// <list type="bullet">
///   <item>Total request count (success and failure).</item>
///   <item>Rolling success/failure counts for health-rate calculation.</item>
///   <item>Cumulative token consumption (input and output).</item>
///   <item>Estimated cost in USD using provider-specific token pricing.</item>
///   <item>Running average latency in milliseconds.</item>
/// </list>
///
/// Cost rates (as at 2025-01 per provider documentation):
/// <list type="bullet">
///   <item>OpenAI GPT-4o-mini — input: $0.15 / 1 M tokens; output: $0.60 / 1 M tokens.</item>
///   <item>Anthropic Claude 3.5 Sonnet — input: $3.00 / 1 M tokens; output: $15.00 / 1 M tokens.</item>
/// </list>
///
/// Use <see cref="GetHealthSnapshot"/> to retrieve a read-only snapshot for health-check
/// endpoints. Use <see cref="LogDailyCostSummary"/> from a scheduled job to emit Serilog
/// structured events for daily cost monitoring.
/// </summary>
public sealed class ProviderHealthTracker
{
    // ── Cost rates per 1 M tokens (AIR-O09) ──────────────────────────────────

    private static readonly IReadOnlyDictionary<string, (decimal InputPer1M, decimal OutputPer1M)>
        CostRates = new Dictionary<string, (decimal, decimal)>(StringComparer.OrdinalIgnoreCase)
        {
            ["openai"]    = (0.15m, 0.60m),
            ["anthropic"] = (3.00m, 15.00m),
        };

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly ConcurrentDictionary<string, ProviderMetrics> _metrics = new(
        StringComparer.OrdinalIgnoreCase);

    private readonly ILogger<ProviderHealthTracker> _logger;

    public ProviderHealthTracker(ILogger<ProviderHealthTracker> logger)
    {
        _logger = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Records the outcome of a single provider request.
    /// Safe to call from multiple concurrent threads.
    /// </summary>
    /// <param name="providerName">Short provider identifier (e.g., "openai", "anthropic").</param>
    /// <param name="success">Whether the provider returned a successful response.</param>
    /// <param name="latencyMs">End-to-end provider round-trip latency in milliseconds.</param>
    /// <param name="inputTokens">Input tokens consumed as reported by the provider.</param>
    /// <param name="outputTokens">Output tokens generated as reported by the provider.</param>
    public void RecordRequest(
        string providerName,
        bool   success,
        long   latencyMs,
        int    inputTokens,
        int    outputTokens)
    {
        var metrics = _metrics.GetOrAdd(providerName, _ => new ProviderMetrics());
        metrics.Record(success, latencyMs, inputTokens, outputTokens);

        var estimatedCost = CalculateCost(providerName, inputTokens, outputTokens);

        _logger.LogDebug(
            "AI Provider request: Provider={Provider} Success={Success} " +
            "LatencyMs={LatencyMs} InputTokens={InputTokens} OutputTokens={OutputTokens} " +
            "EstimatedCostUsd={Cost:F6}",
            providerName, success, latencyMs, inputTokens, outputTokens, estimatedCost);
    }

    /// <summary>
    /// Records a failover event for <paramref name="providerName"/>.
    /// Called by <see cref="CircuitBreakerStateMonitor"/> when the circuit opens.
    /// </summary>
    public void RecordFailoverEvent(string providerName)
    {
        var metrics = _metrics.GetOrAdd(providerName, _ => new ProviderMetrics());
        metrics.RecordFailover();

        _logger.LogWarning(
            "AI Provider failover recorded: Provider={Provider} TotalFailovers={TotalFailovers}",
            providerName, metrics.GetSnapshot().TotalFailovers);
    }

    /// <summary>
    /// Records a recovery for <paramref name="providerName"/> after the circuit closes.
    /// </summary>
    /// <param name="providerName">Provider whose circuit just closed.</param>
    /// <param name="recoveryMs">Outage duration in milliseconds from open to close.</param>
    public void RecordRecovery(string providerName, long recoveryMs)
    {
        var metrics = _metrics.GetOrAdd(providerName, _ => new ProviderMetrics());
        metrics.RecordRecovery(recoveryMs);

        _logger.LogInformation(
            "AI Provider recovery recorded: Provider={Provider} RecoveryMs={RecoveryMs} "
            + "AvgRecoveryMs={AvgRecoveryMs:F1}",
            providerName, recoveryMs, metrics.GetSnapshot().AverageRecoveryMs);
    }

    /// <summary>
    /// Returns a read-only snapshot of the current health metrics for all providers.
    /// </summary>
    public IReadOnlyDictionary<string, ProviderHealthSnapshot> GetHealthSnapshot()
    {
        return _metrics.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.GetSnapshot(),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Emits a structured Serilog log event summarising cumulative token usage and
    /// estimated cost per provider. Intended to be called by a daily scheduled job
    /// or background service (AIR-O09).
    /// </summary>
    public void LogDailyCostSummary()
    {
        if (_metrics.IsEmpty)
        {
            _logger.LogInformation("AI Provider daily cost summary: no requests recorded.");
            return;
        }

        foreach (var (provider, metrics) in _metrics)
        {
            var snapshot  = metrics.GetSnapshot();
            var totalCost = CalculateCost(
                provider, snapshot.TotalInputTokens, snapshot.TotalOutputTokens);

            _logger.LogInformation(
                "AI Provider daily summary: Provider={Provider} " +
                "TotalRequests={TotalRequests} SuccessRate={SuccessRate:P1} " +
                "AvgLatencyMs={AvgLatencyMs:F1} " +
                "TotalInputTokens={TotalInputTokens} TotalOutputTokens={TotalOutputTokens} " +
                "EstimatedDailyCostUsd={TotalCostUsd:F4}",
                provider,
                snapshot.TotalRequests,
                snapshot.SuccessRate,
                snapshot.AverageLatencyMs,
                snapshot.TotalInputTokens,
                snapshot.TotalOutputTokens,
                totalCost);
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static decimal CalculateCost(string providerName, int inputTokens, int outputTokens)
    {
        if (!CostRates.TryGetValue(providerName, out var rates))
            return 0m;

        return (inputTokens  / 1_000_000m * rates.InputPer1M) +
               (outputTokens / 1_000_000m * rates.OutputPer1M);
    }

    // ── Inner types ───────────────────────────────────────────────────────────

    /// <summary>Thread-safe mutable metrics accumulator for a single provider.</summary>
    private sealed class ProviderMetrics
    {
        private long _totalRequests;
        private long _successfulRequests;
        private long _totalLatencyMs;
        private long _totalInputTokens;
        private long _totalOutputTokens;
        private long _totalFailovers;
        private long _totalRecoveries;
        private long _totalRecoveryMs;
        private long _longestOutageMs;

        public void Record(bool success, long latencyMs, int inputTokens, int outputTokens)
        {
            Interlocked.Increment(ref _totalRequests);

            if (success)
                Interlocked.Increment(ref _successfulRequests);

            Interlocked.Add(ref _totalLatencyMs,    latencyMs);
            Interlocked.Add(ref _totalInputTokens,  inputTokens);
            Interlocked.Add(ref _totalOutputTokens, outputTokens);
        }

        public void RecordFailover()
        {
            Interlocked.Increment(ref _totalFailovers);
        }

        public void RecordRecovery(long recoveryMs)
        {
            Interlocked.Increment(ref _totalRecoveries);
            Interlocked.Add(ref _totalRecoveryMs, recoveryMs);

            // Update longest outage (best-effort; monitoring only).
            long current = Interlocked.Read(ref _longestOutageMs);
            if (recoveryMs > current)
                Interlocked.CompareExchange(ref _longestOutageMs, recoveryMs, current);
        }

        public ProviderHealthSnapshot GetSnapshot()
        {
            var total      = Interlocked.Read(ref _totalRequests);
            var success    = Interlocked.Read(ref _successfulRequests);
            var failovers  = Interlocked.Read(ref _totalFailovers);
            var recoveries = Interlocked.Read(ref _totalRecoveries);
            var totalRecMs = Interlocked.Read(ref _totalRecoveryMs);

            return new ProviderHealthSnapshot(
                TotalRequests:    (int)total,
                SuccessfulCount:  (int)success,
                SuccessRate:      total > 0 ? (double)success / total : 0d,
                AverageLatencyMs: total > 0 ? (double)Interlocked.Read(ref _totalLatencyMs) / total : 0d,
                TotalInputTokens:  (int)Interlocked.Read(ref _totalInputTokens),
                TotalOutputTokens: (int)Interlocked.Read(ref _totalOutputTokens),
                TotalFailovers:    (int)failovers,
                TotalRecoveries:   (int)recoveries,
                AverageRecoveryMs: recoveries > 0 ? (double)totalRecMs / recoveries : 0d,
                LongestOutageMs:   Interlocked.Read(ref _longestOutageMs));
        }
    }
}

/// <summary>Read-only snapshot of health metrics for a single provider.</summary>
/// <param name="TotalRequests">Total number of requests sent to this provider.</param>
/// <param name="SuccessfulCount">Number of requests that returned a successful response.</param>
/// <param name="SuccessRate">Ratio of successful to total requests in [0, 1].</param>
/// <param name="AverageLatencyMs">Mean round-trip latency across all recorded requests.</param>
/// <param name="TotalInputTokens">Cumulative input tokens consumed across all requests.</param>
/// <param name="TotalOutputTokens">Cumulative output tokens generated across all requests.</param>
/// <param name="TotalFailovers">Number of times the circuit opened for this provider.</param>
/// <param name="TotalRecoveries">Number of times the circuit closed (recovered).</param>
/// <param name="AverageRecoveryMs">Mean outage duration (open→close) in milliseconds.</param>
/// <param name="LongestOutageMs">Longest outage duration observed in milliseconds.</param>
public sealed record ProviderHealthSnapshot(
    int    TotalRequests,
    int    SuccessfulCount,
    double SuccessRate,
    double AverageLatencyMs,
    int    TotalInputTokens,
    int    TotalOutputTokens,
    int    TotalFailovers    = 0,
    int    TotalRecoveries   = 0,
    double AverageRecoveryMs = 0d,
    long   LongestOutageMs   = 0L);
