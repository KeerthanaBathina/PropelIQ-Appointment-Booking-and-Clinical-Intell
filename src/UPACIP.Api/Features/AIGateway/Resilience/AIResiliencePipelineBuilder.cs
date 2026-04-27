using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;
using UPACIP.Api.Features.AIGateway.Configuration;
using UPACIP.Api.Features.AIGateway.Contracts;

namespace UPACIP.Api.Features.AIGateway.Resilience;

/// <summary>
/// Factory that constructs a Polly V8 <see cref="ResiliencePipeline{T}"/> for a single
/// AI provider (US_067 TASK_002, AIR-O04, AIR-O08).
///
/// Pipeline strategy order (outermost → innermost):
/// <list type="number">
///   <item>
///     <term>Circuit Breaker (outer)</term>
///     <description>
///       Opens after <see cref="ResilienceOptions.CircuitBreakerFailureThreshold"/>
///       consecutive failures within a 30-second sampling window (AIR-O04 = 5 failures).
///       When open, immediately rejects calls without invoking the inner strategies.
///       Breaks for <see cref="ResilienceOptions.CircuitBreakerBreakDurationSeconds"/> (30 s).
///     </description>
///   </item>
///   <item>
///     <term>Retry (inner)</term>
///     <description>
///       Retries up to <see cref="ResilienceOptions.RetryMaxAttempts"/> times with
///       exponential backoff and jitter (AIR-O08 = 3 retries, 1 s base delay).
///     </description>
///   </item>
/// </list>
///
/// Both strategies trigger on:
/// <list type="bullet">
///   <item><see cref="HttpRequestException"/> — network-level transport failures.</item>
///   <item><see cref="TaskCanceledException"/> — request timeout.</item>
///   <item><see cref="AIResponse"/> with <c>Success == false</c> — provider-level failures.</item>
/// </list>
///
/// Call <see cref="Build"/> once per provider at startup; the returned pipeline is
/// long-lived and thread-safe. Circuit-breaker state is held within the pipeline instance.
/// </summary>
public sealed class AIResiliencePipelineBuilder
{
    private readonly ProviderStateManager                 _stateManager;
    private readonly ILogger<AIResiliencePipelineBuilder> _logger;

    public AIResiliencePipelineBuilder(
        ProviderStateManager                  stateManager,
        ILogger<AIResiliencePipelineBuilder>  logger)
    {
        _stateManager = stateManager;
        _logger       = logger;
    }

    /// <summary>
    /// Builds and returns a <see cref="ResiliencePipeline{AIResponse}"/> for the
    /// named provider using the supplied <paramref name="options"/>.
    /// </summary>
    /// <param name="providerName">Short provider name used only in log messages.</param>
    /// <param name="options">Polly resilience policy configuration.</param>
    /// <returns>A thread-safe, long-lived resilience pipeline instance.</returns>
    public ResiliencePipeline<AIResponse> Build(string providerName, ResilienceOptions options)
    {
        return new ResiliencePipelineBuilder<AIResponse>()

            // ── Circuit Breaker (outermost layer) ─────────────────────────────
            // Opening the circuit after FailureThreshold consecutive failures prevents
            // the retry policy from hammering an unavailable provider (AIR-O04).
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<AIResponse>
            {
                // FailureRatio = 1.0 combined with MinimumThroughput = threshold means
                // ALL requests in the sampling window must fail before the circuit opens.
                FailureRatio      = 1.0,
                SamplingDuration  = TimeSpan.FromSeconds(30),
                MinimumThroughput = options.CircuitBreakerFailureThreshold,
                BreakDuration     = TimeSpan.FromSeconds(options.CircuitBreakerBreakDurationSeconds),

                ShouldHandle = new PredicateBuilder<AIResponse>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(r => !r.Success),

                OnOpened = args =>
                {
                    // Circuit open: primary unavailable → route all new requests to fallback.
                    _stateManager.TransitionTo(providerName, ProviderState.Unavailable);
                    _logger.LogError(
                        "AI Gateway: circuit breaker OPENED for provider '{Provider}'. " +
                        "BreakDuration={BreakDurationSecs}s Reason={Reason}",
                        providerName,
                        args.BreakDuration.TotalSeconds,
                        args.Outcome.Exception?.Message ?? "failure ratio exceeded");
                    return ValueTask.CompletedTask;
                },

                OnClosed = args =>
                {
                    // Circuit closed: primary recovered → resume normal routing.
                    _stateManager.TransitionTo(providerName, ProviderState.Active);
                    _logger.LogInformation(
                        "AI Gateway: circuit breaker CLOSED for provider '{Provider}'.",
                        providerName);
                    return ValueTask.CompletedTask;
                },

                OnHalfOpened = args =>
                {
                    // Circuit half-open: send single probe request to test recovery.
                    _stateManager.TransitionTo(providerName, ProviderState.Degraded);
                    _logger.LogInformation(
                        "AI Gateway: circuit breaker HALF-OPEN for provider '{Provider}'. " +
                        "Sending probe request.",
                        providerName);
                    return ValueTask.CompletedTask;
                },
            })

            // ── Retry with exponential backoff (inner layer) ──────────────────
            // Retries transient failures before the circuit breaker counts them (AIR-O08).
            .AddRetry(new RetryStrategyOptions<AIResponse>
            {
                MaxRetryAttempts = options.RetryMaxAttempts,
                BackoffType      = DelayBackoffType.Exponential,
                Delay            = TimeSpan.FromSeconds(options.RetryBaseDelaySeconds),
                UseJitter        = true, // Prevents thundering-herd on simultaneous retries

                ShouldHandle = new PredicateBuilder<AIResponse>()
                    .Handle<HttpRequestException>()
                    .Handle<TaskCanceledException>()
                    .HandleResult(r => !r.Success),

                OnRetry = args =>
                {
                    _logger.LogWarning(
                        "AI Gateway: retrying provider '{Provider}', attempt {Attempt}/{MaxAttempts}. " +
                        "DelayMs={DelayMs:F0} Reason={Reason}",
                        providerName,
                        args.AttemptNumber + 1,
                        options.RetryMaxAttempts,
                        args.RetryDelay.TotalMilliseconds,
                        args.Outcome.Exception?.Message ?? "non-success response");
                    return ValueTask.CompletedTask;
                },
            })

            .Build();
    }
}
