using Polly;
using UPACIP.Service.Resilience.Models;

namespace UPACIP.Service.Resilience;

/// <summary>
/// Centralized registry of named Polly V8 resilience pipelines for external dependencies
/// (US_084 task_001, AC-1).
///
/// <para>
/// Provides three capabilities:
/// <list type="number">
///   <item><see cref="GetPipeline"/> — retrieve the named pipeline for wrapping an operation.</item>
///   <item><see cref="GetCircuitState"/> — read the current circuit state for monitoring.</item>
///   <item><see cref="GetAllCircuitStates"/> — snapshot all circuit states for the admin status API.</item>
/// </list>
/// </para>
///
/// <para>
/// Known dependency names: <c>Sms</c>, <c>Email</c>, <c>AiPrimary</c>, <c>AiFallback</c>.
/// Unknown names return <see cref="ResiliencePipeline.Empty"/> (fail-open).
/// </para>
/// </summary>
public interface IExternalServiceResilienceProvider
{
    /// <summary>
    /// Returns the named Polly V8 resilience pipeline.  Callers pass their operation via
    /// <c>pipeline.ExecuteAsync(async ct => { ... }, cancellationToken)</c>.
    /// Returns <see cref="ResiliencePipeline.Empty"/> when the dependency is not configured
    /// (fail-open behaviour — unknown dependencies are never blocked).
    /// </summary>
    ResiliencePipeline GetPipeline(string dependencyName);

    /// <summary>
    /// Returns the current <see cref="ProviderCircuitState"/> for <paramref name="dependencyName"/>.
    /// Returns <see cref="ProviderCircuitState.Closed"/> when the dependency is not registered.
    /// </summary>
    ProviderCircuitState GetCircuitState(string dependencyName);

    /// <summary>
    /// Returns a point-in-time snapshot of all registered dependency circuit states.
    /// Used by <c>SystemStatusController</c> to expose circuit breaker health to operations staff.
    /// </summary>
    IReadOnlyDictionary<string, ProviderCircuitState> GetAllCircuitStates();
}
