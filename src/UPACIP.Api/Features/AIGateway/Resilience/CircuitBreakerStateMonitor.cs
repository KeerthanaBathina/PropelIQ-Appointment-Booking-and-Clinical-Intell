using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using UPACIP.Api.Features.AIGateway.Models;
using UPACIP.Api.Features.AIGateway.Services;

namespace UPACIP.Api.Features.AIGateway.Resilience;

/// <summary>
/// Subscribes to <see cref="ProviderStateManager.StateChanged"/> events and emits structured
/// Serilog log entries for every circuit-breaker state transition (US_069 TASK_002, AIR-O04).
///
/// Also tracks failover statistics that are exposed via <see cref="GetHealthReport"/>
/// for use by health-check endpoints and the AI provider observability dashboard.
///
/// Registered as Singleton alongside <see cref="ProviderStateManager"/>; the event
/// subscription is wired in the constructor so no transitions are missed.
/// </summary>
public sealed class CircuitBreakerStateMonitor
{
    // ── State tracking ────────────────────────────────────────────────────────

    private readonly ConcurrentDictionary<string, ProviderTransitionMetrics> _metrics =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ProviderStateManager _stateManager;
    private readonly ProviderHealthTracker _healthTracker;
    private readonly ICircuitBreakerStateStore _stateStore;
    private readonly ILogger<CircuitBreakerStateMonitor> _logger;

    public CircuitBreakerStateMonitor(
        ProviderStateManager                  stateManager,
        ProviderHealthTracker                 healthTracker,
        ICircuitBreakerStateStore             stateStore,
        ILogger<CircuitBreakerStateMonitor>   logger)
    {
        _stateManager  = stateManager;
        _healthTracker = healthTracker;
        _stateStore    = stateStore;
        _logger        = logger;

        // Subscribe immediately so no early transitions are missed.
        _stateManager.StateChanged += OnStateChanged;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a current health report for all tracked providers suitable for
    /// surfacing on the <c>/health/ai-providers</c> endpoint.
    /// </summary>
    public IReadOnlyDictionary<string, ProviderHealthReport> GetHealthReport()
    {
        var states = _stateManager.GetAllStates();
        var result = new Dictionary<string, ProviderHealthReport>(StringComparer.OrdinalIgnoreCase);

        foreach (var (provider, state) in states)
        {
            var m = _metrics.GetOrAdd(provider, _ => new ProviderTransitionMetrics());
            var snap = m.GetSnapshot();

            result[provider] = new ProviderHealthReport(
                ProviderName:          provider,
                CurrentState:          state,
                LastTransitionAt:      _stateManager.GetLastTransitionAt(provider),
                TotalFailoverEvents:   snap.TotalFailoverEvents,
                TotalRecoveryEvents:   snap.TotalRecoveryEvents,
                AverageRecoveryMs:     snap.AverageRecoveryMs,
                LongestOutageMs:       snap.LongestOutageMs);
        }

        return result;
    }

    // ── Event handler ─────────────────────────────────────────────────────────

    private void OnStateChanged(object? sender, ProviderStateChangedEventArgs e)
    {
        var m = _metrics.GetOrAdd(e.ProviderName, _ => new ProviderTransitionMetrics());

        // ── Propagate to distributed Redis store (EC-2 cross-instance consistency) ────
        var (cbState, ttl) = MapToCircuitBreakerState(e.NewState);
        // Fire-and-forget: Redis write failures are caught inside SetStateAsync and logged
        // as warnings; they must not block or throw on the Polly callback thread.
        _ = _stateStore.SetStateAsync(e.ProviderName, cbState, ttl);

        // Structured log — all transitions with full context (no PII).
        _logger.LogInformation(
            "AI Gateway circuit breaker: state transition. " +
            "Provider={Provider} From={PreviousState} To={NewState} " +
            "OccurredAt={OccurredAt}",
            e.ProviderName, e.PreviousState, e.NewState, e.OccurredAt);

        // Track failover (Active → Unavailable or Degraded → Unavailable).
        if (e.NewState == ProviderState.Unavailable)
        {
            m.RecordFailover(e.OccurredAt);
            _healthTracker.RecordFailoverEvent(e.ProviderName);

            _logger.LogWarning(
                "AI Gateway: FAILOVER — primary provider '{Provider}' is UNAVAILABLE. " +
                "All new requests will route to fallback. OccurredAt={OccurredAt}",
                e.ProviderName, e.OccurredAt);
        }

        // Track recovery (Degraded → Active).
        if (e.PreviousState == ProviderState.Degraded && e.NewState == ProviderState.Active)
        {
            var recoveryMs = m.RecordRecovery(e.OccurredAt);
            _healthTracker.RecordRecovery(e.ProviderName, recoveryMs);

            _logger.LogInformation(
                "AI Gateway: RECOVERY — primary provider '{Provider}' has RECOVERED. " +
                "RecoveryDurationMs={RecoveryMs} OccurredAt={OccurredAt}",
                e.ProviderName, recoveryMs, e.OccurredAt);
        }

        // Half-open probe started.
        if (e.NewState == ProviderState.Degraded)
        {
            _logger.LogInformation(
                "AI Gateway: PROBING — provider '{Provider}' circuit is HALF-OPEN. " +
                "A single probe request will be sent. OccurredAt={OccurredAt}",
                e.ProviderName, e.OccurredAt);
        }
    }

    // ── Inner types ───────────────────────────────────────────────────────────

    private sealed class ProviderTransitionMetrics
    {
        private long _failoverCount;
        private long _recoveryCount;
        private long _totalRecoveryMs;
        private long _longestOutageMs;
        private DateTimeOffset? _lastFailoverAt;

        public void RecordFailover(DateTimeOffset occurredAt)
        {
            Interlocked.Increment(ref _failoverCount);
            _lastFailoverAt = occurredAt;
        }

        /// <returns>Recovery duration in milliseconds.</returns>
        public long RecordRecovery(DateTimeOffset occurredAt)
        {
            long recoveryMs = 0;
            if (_lastFailoverAt.HasValue)
                recoveryMs = (long)(occurredAt - _lastFailoverAt.Value).TotalMilliseconds;

            Interlocked.Increment(ref _recoveryCount);
            Interlocked.Add(ref _totalRecoveryMs, recoveryMs);

            // Update longest outage (non-atomic but acceptable — monitoring only).
            if (recoveryMs > Interlocked.Read(ref _longestOutageMs))
                Interlocked.Exchange(ref _longestOutageMs, recoveryMs);

            return recoveryMs;
        }

        public (int TotalFailoverEvents, int TotalRecoveryEvents,
                double AverageRecoveryMs, long LongestOutageMs) GetSnapshot()
        {
            var recoveries = (int)Interlocked.Read(ref _recoveryCount);
            var totalMs    = Interlocked.Read(ref _totalRecoveryMs);

            return (
                TotalFailoverEvents: (int)Interlocked.Read(ref _failoverCount),
                TotalRecoveryEvents: recoveries,
                AverageRecoveryMs:   recoveries > 0 ? (double)totalMs / recoveries : 0d,
                LongestOutageMs:     Interlocked.Read(ref _longestOutageMs));
        }
    }

    // ── State mapping helper ──────────────────────────────────────────────────

    /// <summary>
    /// Maps a <see cref="ProviderState"/> to the corresponding
    /// <see cref="CircuitBreakerState"/> and recommended Redis TTL.
    /// </summary>
    private static (CircuitBreakerState State, TimeSpan? Ttl)
        MapToCircuitBreakerState(ProviderState providerState) => providerState switch
    {
        ProviderState.Unavailable => (CircuitBreakerState.Open,     TimeSpan.FromSeconds(60)),
        ProviderState.Degraded    => (CircuitBreakerState.HalfOpen, TimeSpan.FromSeconds(15)),
        ProviderState.Active      => (CircuitBreakerState.Closed,   null),
        _                         => (CircuitBreakerState.Closed,   null),
    };
}

/// <summary>
/// Read-only health report for a single provider, returned by
/// <see cref="CircuitBreakerStateMonitor.GetHealthReport"/>.
/// </summary>
public sealed record ProviderHealthReport(
    string          ProviderName,
    ProviderState   CurrentState,
    DateTimeOffset? LastTransitionAt,
    int             TotalFailoverEvents,
    int             TotalRecoveryEvents,
    double          AverageRecoveryMs,
    long            LongestOutageMs);
