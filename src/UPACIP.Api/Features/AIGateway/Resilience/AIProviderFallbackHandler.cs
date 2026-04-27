using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using UPACIP.Api.Features.AIGateway.Configuration;
using UPACIP.Api.Features.AIGateway.Contracts;
using UPACIP.Api.Features.AIGateway.Models;
using UPACIP.Api.Features.AIGateway.Services;

namespace UPACIP.Api.Features.AIGateway.Resilience;

/// <summary>
/// Singleton service that executes AI requests through a Polly V8 resilience pipeline
/// and transparently routes from the primary provider (OpenAI) to the fallback provider
/// (Claude) without caller awareness (US_067 TASK_002 AC-2, AIR-O04).
///
/// Routing order:
/// <list type="number">
///   <item>Primary provider (OpenAI) — wrapped in its own circuit-breaker + retry pipeline.</item>
///   <item>
///     Fallback provider (Anthropic Claude) — invoked when:
///     <list type="bullet">
///       <item>The primary pipeline returns <c>Success = false</c> after exhausted retries.</item>
///       <item>The primary circuit breaker is open (<see cref="BrokenCircuitException"/>).</item>
///       <item>Any unhandled exception escapes the primary pipeline.</item>
///     </list>
///   </item>
///   <item>
///     Structured error — returned when both providers are unavailable, enabling callers
///     to fall back to the manual workflow (edge case per US_067).
///   </item>
/// </list>
///
/// Both resilience pipelines are built once in the constructor and reused across all
/// requests so that circuit-breaker state persists across concurrent calls (thread-safe).
///
/// <see cref="ProviderHealthTracker"/> is updated after each provider attempt to record
/// request metrics and estimated cost (AIR-O09).
/// </summary>
public sealed class AIProviderFallbackHandler
{
    private readonly IAIProviderAdapter              _primaryProvider;
    private readonly IAIProviderAdapter?             _fallbackProvider;
    private readonly ResiliencePipeline<AIResponse>  _primaryPipeline;
    private readonly ResiliencePipeline<AIResponse>? _fallbackPipeline;
    private readonly ProviderStateManager            _stateManager;
    private readonly ICircuitBreakerStateStore       _stateStore;
    private readonly DegradedModeHandler             _degradedModeHandler;
    private readonly ProviderHealthTracker           _healthTracker;
    private readonly ILogger<AIProviderFallbackHandler> _logger;

    /// <summary>
    /// Resolves the primary and fallback provider adapters from the DI container using
    /// <see cref="AIGatewayOptions.ProviderPriority"/>, then builds a dedicated Polly
    /// resilience pipeline for each (circuit-breaker state is per-pipeline, per-provider).
    /// </summary>
    public AIProviderFallbackHandler(
        IEnumerable<IAIProviderAdapter>  adapters,
        IOptions<AIGatewayOptions>       gatewayOptions,
        IOptions<ResilienceOptions>      resilienceOptions,
        AIResiliencePipelineBuilder      pipelineBuilder,
        ProviderStateManager             stateManager,
        ICircuitBreakerStateStore        stateStore,
        DegradedModeHandler              degradedModeHandler,
        ProviderHealthTracker            healthTracker,
        ILogger<AIProviderFallbackHandler> logger)
    {
        _stateManager        = stateManager;
        _stateStore          = stateStore;
        _degradedModeHandler = degradedModeHandler;
        _healthTracker       = healthTracker;
        _logger              = logger;

        var priority   = gatewayOptions.Value.ProviderPriority;
        var adapterMap = adapters.ToDictionary(
            a => a.ProviderName, StringComparer.OrdinalIgnoreCase);

        // Primary provider (first in priority list)
        var primaryName = priority.Count > 0 ? priority[0] : "openai";
        adapterMap.TryGetValue(primaryName, out var primaryAdapter);
        _primaryProvider = primaryAdapter
            ?? throw new InvalidOperationException(
                $"Primary AI provider '{primaryName}' is not registered. " +
                "Ensure AddAIGateway registers provider adapters before building the pipeline.");

        // Fallback provider (second in priority list, optional)
        if (priority.Count > 1 && adapterMap.TryGetValue(priority[1], out var fallbackAdapter))
        {
            _fallbackProvider = fallbackAdapter;
            _fallbackPipeline = pipelineBuilder.Build(
                fallbackAdapter.ProviderName, resilienceOptions.Value);

            _logger.LogInformation(
                "AI Gateway: resilience pipeline configured. " +
                "Primary={Primary} Fallback={Fallback}",
                _primaryProvider.ProviderName, _fallbackProvider.ProviderName);
        }
        else
        {
            _logger.LogWarning(
                "AI Gateway: no fallback provider configured. " +
                "All failures will return a structured error response.");
        }

        _primaryPipeline = pipelineBuilder.Build(
            _primaryProvider.ProviderName, resilienceOptions.Value);
    }

    /// <summary>
    /// Executes the AI request through the primary provider pipeline, automatically
    /// routing to the fallback provider when the primary fails (AC-2).
    /// Returns a structured error response when both providers are unavailable (edge case).
    /// </summary>
    /// <param name="request">The unified AI request.</param>
    /// <param name="cancellationToken">Propagated from the originating HTTP request.</param>
    /// <returns>
    /// An <see cref="AIResponse"/> with <c>Success = true</c> if any provider succeeded,
    /// or <c>Success = false</c> with <c>ErrorMessage = "AI service unavailable — fallback to
    /// manual workflow"</c> when both providers are unavailable.
    /// </returns>
    public async Task<AIResponse> ExecuteAsync(
        AIRequest         request,
        CancellationToken cancellationToken)
    {
        AIResponse? primaryResult = null;

        // ── Fast-path: check cross-instance Redis state (EC-2) ──────────────────
        // If another instance opened the primary circuit, skip straight to fallback.
        var distributedPrimaryState = await _stateStore.GetStateAsync(
            _primaryProvider.ProviderName, cancellationToken);

        if (distributedPrimaryState == CircuitBreakerState.Open)
        {
            _logger.LogInformation(
                "AI Gateway: primary provider '{Provider}' circuit OPEN (cross-instance Redis state). "
                + "Routing directly to fallback. RequestId={RequestId}",
                _primaryProvider.ProviderName, request.RequestId);

            // Skip to fallback block below.
            goto TryFallback;
        }

        // ── Try primary provider ──────────────────────────────────────────────
        try
        {
            primaryResult = await _primaryPipeline.ExecuteAsync(
                async ct => await _primaryProvider.SendCompletionAsync(request, ct),
                cancellationToken);

            _healthTracker.RecordRequest(
                _primaryProvider.ProviderName,
                primaryResult.Success,
                primaryResult.LatencyMs,
                primaryResult.InputTokensUsed,
                primaryResult.OutputTokensUsed);

            if (primaryResult.Success)
                return primaryResult;

            _logger.LogWarning(
                "AI Gateway: primary provider '{Provider}' returned failure after retries. " +
                "Routing to fallback. RequestId={RequestId} Error={Error}",
                _primaryProvider.ProviderName, request.RequestId,
                primaryResult.ErrorMessage);
        }
        catch (BrokenCircuitException ex)
        {
            // Circuit is open — skip retries and go directly to fallback.
            _logger.LogWarning(
                "AI Gateway: primary provider '{Provider}' circuit is OPEN. " +
                "Routing to fallback without retry. RequestId={RequestId} Reason={Reason}",
                _primaryProvider.ProviderName, request.RequestId, ex.Message);

            _healthTracker.RecordRequest(
                _primaryProvider.ProviderName,
                success: false,
                latencyMs: 0,
                inputTokens: 0,
                outputTokens: 0);
        }
        catch (OperationCanceledException)
        {
            throw; // Propagate cancellation; do not route to fallback
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "AI Gateway: primary provider '{Provider}' pipeline threw unhandled exception. " +
                "Routing to fallback. RequestId={RequestId}",
                _primaryProvider.ProviderName, request.RequestId);

            _healthTracker.RecordRequest(
                _primaryProvider.ProviderName,
                success: false,
                latencyMs: 0,
                inputTokens: 0,
                outputTokens: 0);
        }

        // ── Try fallback provider ─────────────────────────────────────────────
        TryFallback:
        if (_fallbackProvider is not null && _fallbackPipeline is not null)
        {
            _logger.LogWarning(
                "AI Gateway: routing request to fallback provider '{Fallback}'. RequestId={RequestId}",
                _fallbackProvider.ProviderName, request.RequestId);

            AIResponse? fallbackResult = null;

            try
            {
                fallbackResult = await _fallbackPipeline.ExecuteAsync(
                    async ct => await _fallbackProvider.SendCompletionAsync(request, ct),
                    cancellationToken);

                _healthTracker.RecordRequest(
                    _fallbackProvider.ProviderName,
                    fallbackResult.Success,
                    fallbackResult.LatencyMs,
                    fallbackResult.InputTokensUsed,
                    fallbackResult.OutputTokensUsed);

                if (fallbackResult.Success)
                    return fallbackResult;

                _logger.LogError(
                    "AI Gateway: fallback provider '{Provider}' also returned failure. " +
                    "Both providers unavailable. RequestId={RequestId} Error={Error}",
                    _fallbackProvider.ProviderName, request.RequestId,
                    fallbackResult.ErrorMessage);
            }
            catch (BrokenCircuitException ex)
            {
                _logger.LogError(
                    "AI Gateway: fallback provider '{Provider}' circuit is OPEN. " +
                    "Both providers unavailable. RequestId={RequestId} Reason={Reason}",
                    _fallbackProvider.ProviderName, request.RequestId, ex.Message);

                _healthTracker.RecordRequest(
                    _fallbackProvider.ProviderName,
                    success: false,
                    latencyMs: 0,
                    inputTokens: 0,
                    outputTokens: 0);
            }
            catch (OperationCanceledException)
            {
                throw; // Propagate cancellation
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "AI Gateway: fallback provider '{Provider}' pipeline threw unhandled exception. " +
                    "Both providers unavailable. RequestId={RequestId}",
                    _fallbackProvider.ProviderName, request.RequestId);

                _healthTracker.RecordRequest(
                    _fallbackProvider.ProviderName,
                    success: false,
                    latencyMs: 0,
                    inputTokens: 0,
                    outputTokens: 0);
            }
        }

        // ── Both providers unavailable: delegated to DegradedModeHandler (EC-1) ───
        return _degradedModeHandler.HandleDegradedMode(
            request,
            _primaryProvider.ProviderName,
            _fallbackProvider?.ProviderName,
            elapsedMs: 0);
    }
}
