using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.Api.Features.AIGateway.Configuration;
using UPACIP.Api.Features.AIGateway.Contracts;

namespace UPACIP.Api.Features.AIGateway.Providers;

/// <summary>
/// <see cref="IAIProviderAdapter"/> implementation for OpenAI GPT-4o-mini
/// (US_067 TASK_002, AIR-O01, AIR-O02, AIR-O03, AIR-O05).
///
/// Responsibilities:
/// <list type="bullet">
///   <item>Map <see cref="AIRequest"/> to the OpenAI Chat Completions API request body.</item>
///   <item>Parse the OpenAI response into a normalized <see cref="AIResponse"/>.</item>
///   <item>Enforce token budget at adapter level (defense-in-depth, AIR-O01–O03).</item>
///   <item>Handle OpenAI-specific HTTP errors: 429 (rate limit), 500 / 503 (server errors).</item>
///   <item>Support live model-version rollback via <see cref="IOptionsMonitor{T}"/> (AIR-O05).</item>
/// </list>
///
/// Polly resilience wrapping is applied externally by
/// <see cref="UPACIP.Api.Features.AIGateway.Resilience.AIProviderFallbackHandler"/>.
/// This class performs raw HTTP transport only and returns a failed
/// <see cref="AIResponse"/> on provider errors — it does NOT throw on provider failures.
/// </summary>
public sealed class OpenAIProviderAdapter : IAIProviderAdapter
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const string ChatCompletionsPath = "/v1/chat/completions";
    private const string ModelsPath          = "/v1/models";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IHttpClientFactory                    _httpClientFactory;
    private readonly IOptionsMonitor<OpenAIProviderOptions> _optionsMonitor;
    private readonly ILogger<OpenAIProviderAdapter>        _logger;

    public OpenAIProviderAdapter(
        IHttpClientFactory                     httpClientFactory,
        IOptionsMonitor<OpenAIProviderOptions>  optionsMonitor,
        ILogger<OpenAIProviderAdapter>          logger)
    {
        _httpClientFactory = httpClientFactory;
        _optionsMonitor    = optionsMonitor;
        _logger            = logger;
    }

    // ── IAIProviderAdapter ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string ProviderName => "openai";

    /// <inheritdoc/>
    /// <remarks>
    /// Read from <see cref="IOptionsMonitor{T}"/> on every access so a configuration change
    /// (model swap / rollback) takes effect without restart (AIR-O05).
    /// </remarks>
    public string ModelVersion => _optionsMonitor.CurrentValue.Model;

    /// <inheritdoc/>
    public async Task<AIResponse> SendCompletionAsync(
        AIRequest         request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();

        // ── Token budget enforcement (defense-in-depth, AIR-O01–O03) ─────────
        var budget = TokenBudget.For(request.RequestType);

        int effectiveMaxInput  = request.MaxInputTokens  > 0
            ? Math.Min(request.MaxInputTokens, budget.MaxInputTokens)
            : budget.MaxInputTokens;

        int effectiveMaxOutput = request.MaxOutputTokens > 0
            ? Math.Min(request.MaxOutputTokens, budget.MaxOutputTokens)
            : budget.MaxOutputTokens;

        if (request.MaxInputTokens > budget.MaxInputTokens ||
            request.MaxOutputTokens > budget.MaxOutputTokens)
        {
            _logger.LogWarning(
                "OpenAI adapter: token budget violation for {RequestType}. " +
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

        // ── Build OpenAI request body ─────────────────────────────────────────
        var options  = _optionsMonitor.CurrentValue;
        var messages = new List<OpenAIMessage>();

        if (!string.IsNullOrWhiteSpace(request.SystemMessage))
            messages.Add(new OpenAIMessage("system", request.SystemMessage));

        messages.Add(new OpenAIMessage("user", request.Prompt));

        var body = new OpenAIChatRequest(
            Model:       options.Model,
            Messages:    messages,
            MaxTokens:   effectiveMaxOutput,
            Temperature: request.Temperature);

        // ── HTTP call ─────────────────────────────────────────────────────────
        try
        {
            var client  = _httpClientFactory.CreateClient("openai");
            using var response = await client.PostAsJsonAsync(
                ChatCompletionsPath, body, JsonOptions, cancellationToken);

            sw.Stop();

            if (!response.IsSuccessStatusCode)
            {
                var statusCode = (int)response.StatusCode;
                _logger.LogWarning(
                    "OpenAI adapter: non-success HTTP {StatusCode}. " +
                    "RequestId={RequestId} LatencyMs={LatencyMs}",
                    statusCode, request.RequestId, sw.ElapsedMilliseconds);

                var userError = statusCode == 429
                    ? "OpenAI rate limit reached. Request will be retried."
                    : $"OpenAI returned HTTP {statusCode}. Please try again later.";

                return AIResponse.Failed(request.RequestId, userError, sw.ElapsedMilliseconds);
            }

            var completionResponse = await response.Content
                .ReadFromJsonAsync<OpenAIChatResponse>(JsonOptions, cancellationToken);

            if (completionResponse is null ||
                completionResponse.Choices is null ||
                completionResponse.Choices.Count == 0)
            {
                _logger.LogWarning(
                    "OpenAI adapter: empty or null response body. RequestId={RequestId}",
                    request.RequestId);
                return AIResponse.Failed(
                    request.RequestId, "OpenAI returned an empty response.", sw.ElapsedMilliseconds);
            }

            var content = completionResponse.Choices[0].Message?.Content ?? string.Empty;

            // Configuration-alert: model mismatch detection (edge case — API version change)
            if (!string.Equals(completionResponse.Model, options.Model, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "OpenAI adapter: model version mismatch. " +
                    "Configured={Configured} Returned={Returned} RequestId={RequestId}",
                    options.Model, completionResponse.Model, request.RequestId);
            }

            return AIResponse.Succeeded(
                requestId:        request.RequestId,
                content:          content,
                providerName:     ProviderName,
                modelVersion:     completionResponse.Model ?? options.Model,
                inputTokensUsed:  completionResponse.Usage?.PromptTokens     ?? 0,
                outputTokensUsed: completionResponse.Usage?.CompletionTokens ?? 0,
                confidenceScore:  0f,
                latencyMs:        sw.ElapsedMilliseconds);
        }
        catch (OperationCanceledException)
        {
            throw; // Propagate cancellation to caller (do not suppress)
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(
                ex,
                "OpenAI adapter: unhandled exception. RequestId={RequestId} LatencyMs={LatencyMs}",
                request.RequestId, sw.ElapsedMilliseconds);

            return AIResponse.Failed(
                request.RequestId,
                "OpenAI provider encountered an internal error.",
                sw.ElapsedMilliseconds);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Calls the OpenAI models list endpoint. Any HTTP response (including 401/403)
    /// indicates the server is reachable; only network-level failures return false.
    /// This avoids consuming tokens for a health probe.
    /// </remarks>
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client   = _httpClientFactory.CreateClient("openai");
            var response = await client.GetAsync(ModelsPath, cancellationToken);
            // Any HTTP response (even auth errors) means the provider endpoint is reachable.
            return (int)response.StatusCode < 500;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex, "OpenAI adapter: health check failed. Provider may be unreachable.");
            return false;
        }
    }

    // ── Private DTOs (OpenAI Chat Completions API) ────────────────────────────

    private sealed record OpenAIChatRequest(
        [property: JsonPropertyName("model")]       string Model,
        [property: JsonPropertyName("messages")]    List<OpenAIMessage> Messages,
        [property: JsonPropertyName("max_tokens")]  int MaxTokens,
        [property: JsonPropertyName("temperature")] float Temperature);

    private sealed record OpenAIMessage(
        [property: JsonPropertyName("role")]    string Role,
        [property: JsonPropertyName("content")] string Content);

    private sealed record OpenAIChatResponse(
        [property: JsonPropertyName("id")]      string? Id,
        [property: JsonPropertyName("model")]   string? Model,
        [property: JsonPropertyName("choices")] List<OpenAIChoice>? Choices,
        [property: JsonPropertyName("usage")]   OpenAIUsage? Usage);

    private sealed record OpenAIChoice(
        [property: JsonPropertyName("message")]       OpenAIMessage? Message,
        [property: JsonPropertyName("finish_reason")] string? FinishReason);

    private sealed record OpenAIUsage(
        [property: JsonPropertyName("prompt_tokens")]     int PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int CompletionTokens,
        [property: JsonPropertyName("total_tokens")]      int TotalTokens);
}
