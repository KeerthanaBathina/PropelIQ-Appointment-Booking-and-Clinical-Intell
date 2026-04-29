using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using UPACIP.Service.Logging;
using UPACIP.Service.Resilience.Models;

namespace UPACIP.Service.Resilience;

/// <summary>
/// Singleton registry of the three named Polly V8 resilience pipelines for
/// UPACIP transient fault handling (US_095, AC-2, NFR-032, NFR-023).
///
/// Pipelines are built once at application start and are thread-safe for the
/// lifetime of the process (Polly V8 pipeline objects are immutable after build).
///
/// Consumers resolve this registry from DI and call <see cref="GetPipeline"/> or
/// use the typed accessor properties to execute operations with the appropriate policy.
///
/// Usage example (database retry):
/// <code>
/// var result = await _pipelines.Database.ExecuteAsync(
///     async ct => await _db.Patients.FirstOrDefaultAsync(p => p.Id == id, ct),
///     cancellationToken);
/// </code>
/// </summary>
public sealed class TransientResiliencePipelineRegistry
{
    private readonly Dictionary<string, ResiliencePipeline> _pipelines;

    /// <summary>Retry pipeline for transient database errors (no circuit breaker).</summary>
    public ResiliencePipeline Database => _pipelines[ResiliencePipelineConfigurator.PipelineNames.DatabaseRetry];

    /// <summary>Retry + circuit breaker pipeline for outbound HTTP calls.</summary>
    public ResiliencePipeline Http => _pipelines[ResiliencePipelineConfigurator.PipelineNames.HttpRetry];

    /// <summary>Retry + circuit breaker pipeline for non-HTTP external service calls (IO, SMTP direct, sockets).</summary>
    public ResiliencePipeline ExternalService => _pipelines[ResiliencePipelineConfigurator.PipelineNames.ExternalServiceRetry];

    public TransientResiliencePipelineRegistry(
        IOptions<ResilienceOptions>      options,
        ITransientFaultClassifier        classifier,
        ICorrelationIdAccessor           correlationIdAccessor,
        ILogger<TransientResiliencePipelineRegistry> logger)
    {
        var opts = options.Value;

        _pipelines = new Dictionary<string, ResiliencePipeline>(StringComparer.Ordinal)
        {
            [ResiliencePipelineConfigurator.PipelineNames.DatabaseRetry] =
                ResiliencePipelineConfigurator.BuildDatabaseRetryPipeline(opts, classifier, correlationIdAccessor, logger),

            [ResiliencePipelineConfigurator.PipelineNames.HttpRetry] =
                ResiliencePipelineConfigurator.BuildHttpRetryPipeline(opts, classifier, correlationIdAccessor, logger),

            [ResiliencePipelineConfigurator.PipelineNames.ExternalServiceRetry] =
                ResiliencePipelineConfigurator.BuildExternalServiceRetryPipeline(opts, classifier, correlationIdAccessor, logger),
        };

        logger.LogInformation(
            "RESILIENCE_PIPELINES_INITIALIZED: Pipelines={Names}, MaxRetries={MaxRetries}, " +
            "Delays=[{D1}s, {D2}s, {D3}s], CircuitBreakerThreshold={CBThreshold}",
            string.Join(", ", _pipelines.Keys),
            opts.MaxRetries,
            opts.RetryDelaysSeconds.ElementAtOrDefault(0),
            opts.RetryDelaysSeconds.ElementAtOrDefault(1),
            opts.RetryDelaysSeconds.ElementAtOrDefault(2),
            opts.CircuitBreakerFailureThreshold);
    }

    /// <summary>
    /// Retrieves a pipeline by its registered name (see <see cref="ResiliencePipelineConfigurator.PipelineNames"/>).
    /// Returns <see cref="ResiliencePipeline.Empty"/> when the name is not found.
    /// </summary>
    public ResiliencePipeline GetPipeline(string pipelineName)
        => _pipelines.TryGetValue(pipelineName, out var pipeline)
            ? pipeline
            : ResiliencePipeline.Empty;
}
