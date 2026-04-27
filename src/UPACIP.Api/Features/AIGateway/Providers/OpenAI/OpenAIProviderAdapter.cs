using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Chat;
using UPACIP.Api.Features.AIGateway.Configuration;
using UPACIP.Api.Features.AIGateway.Contracts;
using UPACIP.Api.Features.AIGateway.Versioning;

namespace UPACIP.Api.Features.AIGateway.Providers.OpenAI;

/// <summary>
/// Primary <see cref="IAIProviderAdapter"/> implementation backed by the OpenAI .NET SDK
/// <see cref="ChatClient"/> (US_068 TASK_001, AC-1–AC-4, AIR-O01, AIR-O02, AIR-O03, AIR-O04, AIR-O08).
///
/// Responsibilities:
/// <list type="bullet">
///   <item>Enforce per-request token budgets (AIR-O01–O03) as a defense-in-depth layer.</item>
///   <item>Delegate request construction to <see cref="OpenAIRequestMapper"/> (static — testable).</item>
///   <item>Invoke <see cref="ChatClient.CompleteChatAsync"/> via the SDK-managed HTTP transport.</item>
///   <item>Delegate response normalization to <see cref="OpenAIResponseMapper"/> (static — testable).</item>
///   <item>Probe API reachability via GET /v1/models in <see cref="IsHealthyAsync"/>.</item>
/// </list>
///
/// Polly retry/circuit-breaker resilience (AIR-O08: max 3 retries, exponential back-off, jitter)
/// is applied externally by
/// <see cref="UPACIP.Api.Features.AIGateway.Resilience.AIProviderFallbackHandler"/>.
/// This class performs transport only and returns a failed <see cref="AIResponse"/> on provider
/// errors rather than propagating exceptions (except <see cref="OperationCanceledException"/>).
/// </summary>
public sealed class OpenAIProviderAdapter : IAIProviderAdapter
{
    private const string ModelsPath = "/v1/models";

    private readonly ChatClient                               _chatClient;
    private readonly IHttpClientFactory                       _httpClientFactory;
    private readonly IOptionsMonitor<OpenAIProviderOptions>   _optionsMonitor;
    private readonly IModelVersionRegistry                    _versionRegistry;
    private readonly ILogger<OpenAIProviderAdapter>           _logger;

    public OpenAIProviderAdapter(
        ChatClient                               chatClient,
        IHttpClientFactory                       httpClientFactory,
        IOptionsMonitor<OpenAIProviderOptions>   optionsMonitor,
        IModelVersionRegistry                    versionRegistry,
        ILogger<OpenAIProviderAdapter>           logger)
    {
        _chatClient        = chatClient;
        _httpClientFactory = httpClientFactory;
        _optionsMonitor    = optionsMonitor;
        _versionRegistry   = versionRegistry;
        _logger            = logger;

        // Log configuration changes so rollbacks are visible in the operational log.
        _optionsMonitor.OnChange(opts =>
            _logger.LogInformation(
                "OpenAI SDK adapter: configuration changed. Model={Model}",
                opts.Model));
    }

    // ── IAIProviderAdapter ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string ProviderName => "openai";

    /// <inheritdoc/>
    /// <remarks>
    /// Resolved from <see cref="IModelVersionRegistry.GetActiveVersion"/> so that an admin
    /// rollback takes effect on the very next request without a service restart (AIR-O05).
    /// </remarks>
    public string ModelVersion => _versionRegistry.GetActiveVersion(ProviderName);

    /// <inheritdoc/>
    public async Task<AIResponse> SendCompletionAsync(
        AIRequest         request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        // ── Token budget enforcement (defense-in-depth, AIR-O01–O03) ─────────
        var budget = TokenBudget.For(request.RequestType);

        int effectiveMaxOutput = request.MaxOutputTokens > 0
            ? Math.Min(request.MaxOutputTokens, budget.MaxOutputTokens)
            : budget.MaxOutputTokens;

        if (request.MaxInputTokens > budget.MaxInputTokens ||
            request.MaxOutputTokens > budget.MaxOutputTokens)
        {
            _logger.LogWarning(
                "OpenAI SDK adapter: token budget violation for {RequestType}. " +
                "RequestedInput={ReqIn} BudgetInput={BudIn} " +
                "RequestedOutput={ReqOut} BudgetOutput={BudOut} RequestId={RequestId}",
                request.RequestType,
                request.MaxInputTokens, budget.MaxInputTokens,
                request.MaxOutputTokens, budget.MaxOutputTokens,
                request.RequestId);

            return AIResponse.Failed(
                request.RequestId,
                $"Token budget exceeded for '{request.RequestType}': " +
                $"input limit {budget.MaxInputTokens}, output limit {budget.MaxOutputTokens}.",
                sw.ElapsedMilliseconds);
        }

        // ── Build SDK request objects via static mappers ──────────────────────
        var messages       = OpenAIRequestMapper.MapToMessages(request);
        var completionOpts = OpenAIRequestMapper.MapToOptions(request, effectiveMaxOutput);

        // ── Invoke OpenAI SDK ChatClient ──────────────────────────────────────
        try
        {
            var result = await _chatClient.CompleteChatAsync(
                messages, completionOpts, cancellationToken);

            sw.Stop();
            var completion = result.Value;

            _logger.LogInformation(
                "OpenAI SDK adapter: completion succeeded. " +
                "RequestId={RequestId} Model={Model} " +
                "InputTokens={InputTokens} OutputTokens={OutputTokens} LatencyMs={LatencyMs}",
                request.RequestId,
                completion.Model,
                completion.Usage?.InputTokenCount,
                completion.Usage?.OutputTokenCount,
                sw.ElapsedMilliseconds);

            return OpenAIResponseMapper.MapToResponse(
                completion, request, ProviderName, sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw; // Propagate cancellation — do not convert to a failed response.
        }
        catch (System.ClientModel.ClientResultException ex)
        {
            sw.Stop();

            var userError = ex.Status == 429
                ? "OpenAI rate limit reached. Request will be retried."
                : $"OpenAI returned an API error (HTTP {ex.Status}). Please try again later.";

            _logger.LogWarning(
                "OpenAI SDK adapter: API error HTTP {StatusCode}. " +
                "RequestId={RequestId} LatencyMs={LatencyMs}",
                ex.Status, request.RequestId, sw.ElapsedMilliseconds);

            return AIResponse.Failed(request.RequestId, userError, sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(
                ex,
                "OpenAI SDK adapter: unhandled exception. " +
                "RequestId={RequestId} LatencyMs={LatencyMs}",
                request.RequestId, sw.ElapsedMilliseconds);

            return AIResponse.Failed(
                request.RequestId,
                "OpenAI provider encountered an internal error.",
                sw.ElapsedMilliseconds);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Calls GET /v1/models via the pre-configured "openai" named <see cref="HttpClient"/>
    /// (auth header already set in Program.cs).
    ///
    /// Any HTTP response — including 401/403 auth errors — means the endpoint is reachable
    /// and returns <see langword="true"/>. Only network-level failures (DNS, connect timeout)
    /// return <see langword="false"/>. This avoids consuming tokens for health probes.
    /// </remarks>
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client   = _httpClientFactory.CreateClient("openai");
            var response = await client.GetAsync(ModelsPath, cancellationToken);
            return (int)response.StatusCode < 500;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "OpenAI SDK adapter: health check failed. Provider may be unreachable.");
            return false;
        }
    }
}
