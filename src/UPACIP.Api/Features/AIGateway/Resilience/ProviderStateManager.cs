using System.Collections.Concurrent;

namespace UPACIP.Api.Features.AIGateway.Resilience;

/// <summary>
/// Possible health states of an AI provider as seen by the circuit breaker
/// (US_069 TASK_002, AIR-O04, AIR-O05).
/// </summary>
public enum ProviderState
{
    /// <summary>Circuit closed — provider is receiving traffic normally.</summary>
    Active = 0,

    /// <summary>Circuit half-open — a single probe request is being sent to test recovery.</summary>
    Degraded = 1,

    /// <summary>Circuit open — provider is unavailable; all traffic is routed to fallback.</summary>
    Unavailable = 2,
}

/// <summary>
/// Payload carried by the <see cref="ProviderStateManager.StateChanged"/> event.
/// </summary>
/// <param name="ProviderName">Short provider identifier (e.g., <c>"openai"</c>).</param>
/// <param name="PreviousState">State before the transition.</param>
/// <param name="NewState">State after the transition.</param>
/// <param name="OccurredAt">UTC timestamp of the transition.</param>
public sealed record ProviderStateChangedEventArgs(
    string        ProviderName,
    ProviderState PreviousState,
    ProviderState NewState,
    DateTimeOffset OccurredAt);

/// <summary>
/// Thread-safe singleton that tracks the health state of each AI provider and
/// publishes state-transition events consumed by <see cref="CircuitBreakerStateMonitor"/>
/// (US_069 TASK_002, AIR-O04, AIR-O05).
///
/// State machine per provider:
/// <code>
///   Active → Unavailable  (circuit opened after MinimumThroughput failures)
///   Unavailable → Degraded (circuit half-opened after BreakDuration elapsed)
///   Degraded → Active      (probe succeeded — circuit closed)
///   Degraded → Unavailable (probe failed — circuit re-opened)
/// </code>
///
/// Transitions are driven by Polly circuit breaker callbacks in
/// <see cref="AIResiliencePipelineBuilder.Build"/>.
/// </summary>
public sealed class ProviderStateManager
{
    private readonly ConcurrentDictionary<string, ProviderState> _states =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastTransitionAt =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Raised on every successful state transition.
    /// Subscribers (e.g., <see cref="CircuitBreakerStateMonitor"/>) must be registered
    /// before provider pipelines are built to ensure no events are missed.
    /// Thread-safety: event dispatch uses the standard .NET multicast delegate mechanism.
    /// </summary>
    public event EventHandler<ProviderStateChangedEventArgs>? StateChanged;

    /// <summary>
    /// Returns the current <see cref="ProviderState"/> for <paramref name="providerName"/>.
    /// Defaults to <see cref="ProviderState.Active"/> when no state has been recorded.
    /// </summary>
    public ProviderState GetProviderState(string providerName) =>
        _states.TryGetValue(providerName, out var state) ? state : ProviderState.Active;

    /// <summary>
    /// Returns <see langword="true"/> when the primary provider is
    /// <see cref="ProviderState.Active"/> (circuit closed).
    /// </summary>
    public bool IsPrimaryHealthy(string primaryProviderName) =>
        GetProviderState(primaryProviderName) == ProviderState.Active;

    /// <summary>
    /// Transitions <paramref name="providerName"/> to <paramref name="newState"/> if
    /// the state has actually changed. The transition is atomic: the update and event
    /// dispatch happen together under no external lock, relying on <see cref="ConcurrentDictionary"/>
    /// for thread-safety at the slot level.
    /// </summary>
    public void TransitionTo(string providerName, ProviderState newState)
    {
        var previous = _states.GetOrAdd(providerName, ProviderState.Active);
        if (previous == newState)
            return; // No-op: already in target state

        _states[providerName] = newState;
        var now = DateTimeOffset.UtcNow;
        _lastTransitionAt[providerName] = now;

        StateChanged?.Invoke(this, new ProviderStateChangedEventArgs(
            ProviderName:  providerName,
            PreviousState: previous,
            NewState:      newState,
            OccurredAt:    now));
    }

    /// <summary>
    /// Returns a read-only snapshot of all tracked provider states at the moment of call.
    /// </summary>
    public IReadOnlyDictionary<string, ProviderState> GetAllStates() =>
        new Dictionary<string, ProviderState>(_states, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the UTC timestamp of the most recent state transition for
    /// <paramref name="providerName"/>, or <see langword="null"/> if no transition has occurred.
    /// </summary>
    public DateTimeOffset? GetLastTransitionAt(string providerName) =>
        _lastTransitionAt.TryGetValue(providerName, out var ts) ? ts : null;
}
