using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using Polly.Timeout;
using UPACIP.Service.Performance;
using UPACIP.Service.Resilience.Models;

namespace UPACIP.Service.Resilience;

/// <summary>
/// Singleton Polly V8 resilience pipeline registry for external dependencies
/// (US_084 task_001, AC-1).
///
/// <para>
/// Builds one named pipeline per configured dependency in the constructor. Each pipeline
/// composes three strategies (outermost → innermost):
/// <list type="number">
///   <item><strong>Circuit Breaker</strong> — opens after <c>FailureThreshold</c> consecutive
///     failures; breaks for <c>BreakDurationSeconds</c>. State transitions are logged and
///     recorded as performance metrics.</item>
///   <item><strong>Retry</strong> — retries up to <c>RetryCount</c> times with exponential
///     backoff + jitter. <see cref="BrokenCircuitException"/> is excluded from the retry
///     predicate to avoid re-entering an open circuit.</item>
///   <item><strong>Timeout</strong> — per-call limit of <c>TimeoutSeconds</c> seconds.</item>
/// </list>
/// </para>
///
/// <para>
/// Circuit state transitions are tracked in a <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// and exposed via <see cref="GetCircuitState"/> / <see cref="GetAllCircuitStates"/> for the
/// admin system status API (US_083 task_002 integration).
/// </para>
/// </summary>
public sealed class ExternalServiceResilienceProvider : IExternalServiceResilienceProvider
{
    private readonly ConcurrentDictionary<string, ResiliencePipeline>    _pipelines;
    private readonly ConcurrentDictionary<string, ProviderCircuitState>  _circuitStates;
    private readonly ILogger<ExternalServiceResilienceProvider>          _logger;

    public ExternalServiceResilienceProvider(
        IOptions<ExternalServiceResilienceOptions>      options,
        IPerformanceTracker                             performanceTracker,
        ILogger<ExternalServiceResilienceProvider>      logger)
    {
        _logger        = logger;
        _pipelines     = new ConcurrentDictionary<string, ResiliencePipeline>(StringComparer.OrdinalIgnoreCase);
        _circuitStates = new ConcurrentDictionary<string, ProviderCircuitState>(StringComparer.OrdinalIgnoreCase);

        foreach (var (name, config) in options.Value.Dependencies)
        {
            _circuitStates[name] = ProviderCircuitState.Closed;
            _pipelines[name]     = BuildPipeline(name, config, performanceTracker);
        }
    }

    // ── IExternalServiceResilienceProvider ───────────────────────────────────

    /// <inheritdoc />
    public ResiliencePipeline GetPipeline(string dependencyName)
        => _pipelines.TryGetValue(dependencyName, out var pipeline)
            ? pipeline
            : ResiliencePipeline.Empty;

    /// <inheritdoc />
    public ProviderCircuitState GetCircuitState(string dependencyName)
        => _circuitStates.TryGetValue(dependencyName, out var state) ? state : ProviderCircuitState.Closed;

    /// <inheritdoc />
    public IReadOnlyDictionary<string, ProviderCircuitState> GetAllCircuitStates()
        => _circuitStates;

    // ── Private pipeline builder ─────────────────────────────────────────────

    private ResiliencePipeline BuildPipeline(
        string              name,
        CircuitBreakerConfig config,
        IPerformanceTracker  tracker)
    {
        return new ResiliencePipelineBuilder()

            // ── Circuit Breaker (outermost) ───────────────────────────────────
            // Opens after FailureThreshold consecutive failures within a sampling window
            // equal to BreakDurationSeconds (NFR-023, AC-1).
            // BrokenCircuitException escapes directly to the caller — the retry inner
            // layer never sees it because this is the outermost strategy.
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                FailureRatio      = 1.0,
                SamplingDuration  = TimeSpan.FromSeconds(config.BreakDurationSeconds),
                MinimumThroughput = config.FailureThreshold,
                BreakDuration     = TimeSpan.FromSeconds(config.BreakDurationSeconds),

                ShouldHandle = new PredicateBuilder().Handle<Exception>(),

                OnOpened = args =>
                {
                    _circuitStates[name] = ProviderCircuitState.Open;
                    _logger.LogWarning(
                        "CIRCUIT_BREAKER: {Dependency} transitioned Closed → Open. " +
                        "BreakDuration={BreakSecs}s Reason={Reason}",
                        name,
                        config.BreakDurationSeconds,
                        args.Outcome.Exception?.Message ?? "failure ratio exceeded");
                    tracker.RecordLatency($"circuit.{name}.state_change", 1);
                    return ValueTask.CompletedTask;
                },

                OnClosed = args =>
                {
                    _circuitStates[name] = ProviderCircuitState.Closed;
                    _logger.LogInformation(
                        "CIRCUIT_BREAKER: {Dependency} transitioned → Closed (recovered).",
                        name);
                    tracker.RecordLatency($"circuit.{name}.state_change", 1);
                    return ValueTask.CompletedTask;
                },

                OnHalfOpened = args =>
                {
                    _circuitStates[name] = ProviderCircuitState.HalfOpen;
                    _logger.LogInformation(
                        "CIRCUIT_BREAKER: {Dependency} transitioned → HalfOpen (probe).",
                        name);
                    return ValueTask.CompletedTask;
                },
            })

            // ── Retry (middle layer) ──────────────────────────────────────────
            // Retries transient failures before the circuit breaker counts them.
            // BrokenCircuitException is excluded: it must escape to the outermost level.
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = config.RetryCount,
                BackoffType      = DelayBackoffType.Exponential,
                Delay            = TimeSpan.FromMilliseconds(config.RetryBaseDelayMs),
                UseJitter        = true,

                ShouldHandle = new PredicateBuilder()
                    .Handle<Exception>(ex => ex is not BrokenCircuitException),

                OnRetry = args =>
                {
                    _logger.LogWarning(
                        "CIRCUIT_BREAKER: {Dependency} retry attempt {Attempt}/{Max}. " +
                        "DelayMs={DelayMs:F0} Reason={Reason}",
                        name,
                        args.AttemptNumber + 1,
                        config.RetryCount,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception?.Message ?? "transient failure");
                    return ValueTask.CompletedTask;
                },
            })

            // ── Timeout (innermost — per-call) ────────────────────────────────
            .AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds),
            })

            .Build();
    }
}
