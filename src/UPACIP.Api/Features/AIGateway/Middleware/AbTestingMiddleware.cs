using Microsoft.Extensions.Logging;
using UPACIP.Api.Features.AIGateway.Contracts;
using UPACIP.Service.AiTesting;
using UPACIP.Service.AiTesting.Models;

namespace UPACIP.Api.Features.AIGateway.Middleware;

/// <summary>
/// AI Gateway middleware component that intercepts AI requests, assigns the caller to a
/// model variant, overrides the model ID in the request metadata, and records per-request
/// metrics after the response is received (US_080 task_001, AC-1, AC-2, AIR-O10).
///
/// <para>
/// <b>Pipeline position (in AI Gateway flow)</b>:
/// <list type="number">
///   <item>Prompt injection sanitization</item>
///   <item>PII redaction</item>
///   <item><b>A/B variant assignment and model override ← this middleware</b></item>
///   <item>Token budget enforcement</item>
///   <item>Provider dispatch (HTTP call to OpenAI / Anthropic)</item>
///   <item>Response normalization</item>
///   <item>Content filtering</item>
/// </list>
/// </para>
///
/// <para>
/// When no experiment is active the middleware passes the request through unchanged
/// and attaches no observability headers.
/// </para>
///
/// <para>
/// Response headers added when an experiment is active:
/// <list type="bullet">
///   <item><c>X-Ab-Experiment: {experimentId}</c></item>
///   <item><c>X-Ab-Variant: Control | Candidate</c></item>
/// </list>
/// </para>
///
/// <para>Scoped lifetime — depends on scoped <see cref="IAbTestingService"/>.</para>
/// </summary>
public sealed class AbTestingMiddleware
{
    private readonly IAbTestingService              _abService;
    private readonly ILogger<AbTestingMiddleware>   _logger;

    public AbTestingMiddleware(
        IAbTestingService            abService,
        ILogger<AbTestingMiddleware> logger)
    {
        _abService = abService;
        _logger    = logger;
    }

    /// <summary>
    /// Processes an AI request for A/B routing: assigns a variant, optionally overrides the
    /// model identifier in request metadata, dispatches via <paramref name="invokeProvider"/>,
    /// then records per-request metrics.
    /// </summary>
    /// <param name="request">Incoming AI Gateway request.</param>
    /// <param name="userId">Authenticated user identifier for deterministic variant assignment.</param>
    /// <param name="requestType">Request type string for metric recording (e.g. <c>document-parsing</c>).</param>
    /// <param name="invokeProvider">
    /// Async delegate that dispatches the (possibly metadata-enriched) request to the AI provider.
    /// </param>
    /// <param name="setResponseHeader">
    /// Callback to attach A/B observability headers to the HTTP response.
    /// </param>
    /// <param name="ct">Propagates cancellation.</param>
    /// <returns>
    /// The AI response, plus the assigned variant (null when no active experiment).
    /// </returns>
    public async Task<(AIResponse response, AbVariant? variant)> InvokeAsync(
        AIRequest                                             request,
        string                                               userId,
        string                                               requestType,
        Func<AIRequest, CancellationToken, Task<AIResponse>> invokeProvider,
        Action<string, string>                               setResponseHeader,
        CancellationToken                                    ct = default)
    {
        var experiment = await _abService.GetActiveExperimentAsync(ct);

        if (experiment is null || experiment.Status != AbExperimentStatus.Active)
        {
            var passthroughResponse = await invokeProvider(request, ct);
            return (passthroughResponse, null);
        }

        // Deterministic variant assignment.
        var variant = AbTestingService.ComputeVariant(
            experiment.Id, userId, experiment.TrafficSplitPercentage);

        var modelId = variant == AbVariant.Candidate
            ? experiment.CandidateModelId
            : experiment.ControlModelId;

        // Inject A/B metadata so the provider layer can apply the model override.
        var metadata = new Dictionary<string, string>(request.Metadata)
        {
            ["AbModelOverride"] = modelId,
            ["AbExperimentId"]  = experiment.Id.ToString(),
            ["AbVariant"]       = variant.ToString(),
        };

        var modifiedRequest = new AIRequest
        {
            RequestId        = request.RequestId,
            RequestType      = request.RequestType,
            Prompt           = request.Prompt,
            SystemMessage    = request.SystemMessage,
            MaxInputTokens   = request.MaxInputTokens,
            MaxOutputTokens  = request.MaxOutputTokens,
            Temperature      = request.Temperature,
            CorrelationId    = request.CorrelationId,
            Metadata         = metadata,
        };

        // Attach observability headers.
        setResponseHeader("X-Ab-Experiment", experiment.Id.ToString());
        setResponseHeader("X-Ab-Variant",    variant.ToString());

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var response = await invokeProvider(modifiedRequest, ct);
        sw.Stop();

        // Record metric without blocking the response path.
        var tokensUsed = response.InputTokensUsed + response.OutputTokensUsed;
        var cost       = EstimateCost(modelId, response.InputTokensUsed, response.OutputTokensUsed);

        _ = RecordMetricSafeAsync(
            experiment.Id, variant, requestType,
            sw.ElapsedMilliseconds, tokensUsed, cost,
            CancellationToken.None); // None — record even if request is cancelled.

        return (response, variant);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task RecordMetricSafeAsync(
        Guid      experimentId,
        AbVariant variant,
        string    requestType,
        long      latencyMs,
        int       tokensUsed,
        decimal   cost,
        CancellationToken ct)
    {
        try
        {
            await _abService.RecordMetricAsync(new AbMetricRecord
            {
                ExperimentId  = experimentId,
                Variant       = variant,
                RequestType   = requestType,
                LatencyMs     = latencyMs,
                TokensUsed    = tokensUsed,
                EstimatedCost = cost,
                // Accuracy is null at request time; set by downstream validators.
            }, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AbTestingMiddleware: failed to record metric. ExperimentId={Id} Variant={Variant}",
                experimentId, variant);
        }
    }

    /// <summary>
    /// Estimates monetary cost. Uses gpt-4o-mini rates by default;
    /// Claude rates for models containing "claude".
    /// </summary>
    private static decimal EstimateCost(string modelId, int inputTokens, int outputTokens)
    {
        decimal inputPer1M  = 0.15m;
        decimal outputPer1M = 0.60m;

        if (modelId.Contains("claude", StringComparison.OrdinalIgnoreCase))
        {
            inputPer1M  = 3.00m;
            outputPer1M = 15.00m;
        }

        return (inputTokens  * inputPer1M  / 1_000_000m)
             + (outputTokens * outputPer1M / 1_000_000m);
    }
}
