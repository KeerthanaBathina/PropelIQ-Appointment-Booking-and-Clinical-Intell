using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.Api.Features.AIGateway.Configuration;
using UPACIP.Api.Features.AIGateway.Contracts;
using UPACIP.Api.Features.AIGateway.Versioning;

namespace UPACIP.Api.Features.AIGateway.Providers;

/// <summary>
/// <see cref="IAIProviderAdapter"/> implementation for Anthropic Claude 3.5 Sonnet —
/// the automatic fallback provider when the primary OpenAI circuit breaker is open
/// or retries are exhausted (US_069 TASK_001, AC-1, AC-2, AIR-O01–AIR-O03, AIR-O05).
///
/// Responsibilities:
/// <list type="bullet">
///   <item>Delegate request construction to <see cref="ClaudeRequestBuilder"/> (injectable, testable).</item>
///   <item>Enforce per-request token budgets (AIR-O01–AIR-O03) as a defense-in-depth layer.</item>
///   <item>Invoke the Anthropic Messages API via the pre-configured "anthropic" named HttpClient.</item>
///   <item>Delegate response normalization to <see cref="ClaudeResponseMapper"/> (injectable, testable).</item>
///   <item>Handle Anthropic-specific HTTP errors: 429 (rate limit), 529 (overloaded), 401, 500.</item>
///   <item>Log all fallback invocations with latency, token usage for audit (AIR-S01, AIR-O09).</item>
///   <item>Support live model-version rollback via <see cref="IOptionsMonitor{T}"/> (AIR-O05).</item>
/// </list>
///
/// Polly resilience wrapping (circuit-breaker + retry, AIR-O08) is applied externally by
/// <see cref="UPACIP.Api.Features.AIGateway.Resilience.AIProviderFallbackHandler"/>.
/// This class performs transport only and returns a failed <see cref="AIResponse"/> on
/// provider errors — it does NOT throw on provider failures (except <see cref="OperationCanceledException"/>).
/// </summary>
public sealed class ClaudeProviderAdapter : IAIProviderAdapter
{
    // ── Constants ─────────────────────────────────────────────────────────────

    private const string MessagesPath = "/v1/messages";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IHttpClientFactory                    _httpClientFactory;
    private readonly IOptionsMonitor<ClaudeProviderOptions> _optionsMonitor;
    private readonly ClaudeRequestBuilder                  _requestBuilder;
    private readonly ClaudeResponseMapper                  _responseMapper;
    private readonly IModelVersionRegistry                 _versionRegistry;
    private readonly ILogger<ClaudeProviderAdapter>        _logger;

    public ClaudeProviderAdapter(
        IHttpClientFactory                     httpClientFactory,
        IOptionsMonitor<ClaudeProviderOptions>  optionsMonitor,
        ClaudeRequestBuilder                   requestBuilder,
        ClaudeResponseMapper                   responseMapper,
        IModelVersionRegistry                  versionRegistry,
        ILogger<ClaudeProviderAdapter>          logger)
    {
        _httpClientFactory = httpClientFactory;
        _optionsMonitor    = optionsMonitor;
        _requestBuilder    = requestBuilder;
        _responseMapper    = responseMapper;
        _versionRegistry   = versionRegistry;
        _logger            = logger;
    }

    // ── IAIProviderAdapter ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public string ProviderName => "anthropic";

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

        _logger.LogDebug(
            "Claude adapter: starting completion. " +
            "CorrelationId={CorrelationId} RequestType={RequestType} RequestId={RequestId}",
            request.CorrelationId, request.RequestType, request.RequestId);

        // ── Token budget enforcement (defense-in-depth, AIR-O01–O03) ─────────
        // AIGatewayService also calls TokenBudgetEnforcementService upstream;
        // this layer is a final guard before bytes leave the process.
        var budget = TokenBudget.For(request.RequestType);

        int effectiveMaxOutput = request.MaxOutputTokens > 0
            ? Math.Min(request.MaxOutputTokens, budget.MaxOutputTokens)
            : budget.MaxOutputTokens;

        if (request.MaxInputTokens > budget.MaxInputTokens ||
            request.MaxOutputTokens > budget.MaxOutputTokens)
        {
            _logger.LogWarning(
                "Claude adapter: token budget violation for {RequestType}. " +
                "RequestedInput={ReqIn} BudgetInput={BudIn} " +
                "RequestedOutput={ReqOut} BudgetOutput={BudOut} " +
                "CorrelationId={CorrelationId} RequestId={RequestId}",
                request.RequestType,
                request.MaxInputTokens, budget.MaxInputTokens,
                request.MaxOutputTokens, budget.MaxOutputTokens,
                request.CorrelationId, request.RequestId);

            return AIResponse.Failed(
                request.RequestId,
                $"Token budget exceeded for '{request.RequestType}': " +
                $"input limit {budget.MaxInputTokens}, output limit {budget.MaxOutputTokens}.",
                sw.ElapsedMilliseconds);
        }

        // ── Build Anthropic Messages API payload via ClaudeRequestBuilder ─────
        var options = _optionsMonitor.CurrentValue;
        var payload = _requestBuilder.Build(request, options.Model, effectiveMaxOutput);

        // ── HTTP call ─────────────────────────────────────────────────────────
        try
        {
            var client = _httpClientFactory.CreateClient("anthropic");
            using var httpResponse = await client.PostAsJsonAsync(
                MessagesPath, payload, JsonOptions, cancellationToken);

            sw.Stop();

            if (!httpResponse.IsSuccessStatusCode)
            {
                var statusCode = (int)httpResponse.StatusCode;

                // Read Retry-After header for rate-limit logging (AIR-O07).
                string? retryAfter = null;
                if (statusCode == 429)
                    retryAfter = httpResponse.Headers.RetryAfter?.Delta?.ToString()
                              ?? httpResponse.Headers.RetryAfter?.Date?.ToString();

                _logger.LogWarning(
                    "Claude adapter: non-success HTTP {StatusCode}. " +
                    "CorrelationId={CorrelationId} RequestId={RequestId} " +
                    "LatencyMs={LatencyMs} RetryAfter={RetryAfter}",
                    statusCode, request.CorrelationId, request.RequestId,
                    sw.ElapsedMilliseconds, retryAfter ?? "N/A");

                var userError = statusCode switch
                {
                    401 => "Anthropic API key is invalid or revoked.",
                    429 => "Anthropic rate limit reached. Request will be retried.",
                    529 => "Anthropic API is overloaded. Request will be retried.",
                    _   => $"Anthropic returned HTTP {statusCode}. Please try again later.",
                };

                return AIResponse.Failed(request.RequestId, userError, sw.ElapsedMilliseconds);
            }

            var messagesResponse = await httpResponse.Content
                .ReadFromJsonAsync<ClaudeMessagesResponseBody>(JsonOptions, cancellationToken);

            if (messagesResponse is null ||
                messagesResponse.Content is null ||
                messagesResponse.Content.Count == 0)
            {
                _logger.LogWarning(
                    "Claude adapter: empty or null response body. " +
                    "CorrelationId={CorrelationId} RequestId={RequestId}",
                    request.CorrelationId, request.RequestId);

                return AIResponse.Failed(
                    request.RequestId,
                    "Anthropic returned an empty response.",
                    sw.ElapsedMilliseconds);
            }

            // Log stop_reason = max_tokens as a truncation warning (AC-2 edge case).
            if (ClaudeResponseMapper.IsOutputTruncated(messagesResponse))
            {
                _logger.LogWarning(
                    "Claude adapter: output truncated by max_tokens ceiling. " +
                    "CorrelationId={CorrelationId} RequestId={RequestId} " +
                    "MaxOutputTokens={MaxOutputTokens}",
                    request.CorrelationId, request.RequestId, effectiveMaxOutput);
            }

            // Configuration-alert: model version mismatch detection (AIR-O05 edge case).
            if (!string.Equals(messagesResponse.Model, options.Model, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Claude adapter: model version mismatch. " +
                    "Configured={Configured} Returned={Returned} " +
                    "CorrelationId={CorrelationId} RequestId={RequestId}",
                    options.Model, messagesResponse.Model,
                    request.CorrelationId, request.RequestId);
            }

            var response = _responseMapper.MapToResponse(
                messagesResponse, request, ProviderName, sw.ElapsedMilliseconds);

            // Structured audit log — token usage for cost tracking (AIR-O09, AIR-S01).
            // No prompt content logged: PII-safe.
            _logger.LogInformation(
                "Claude adapter: completion succeeded. " +
                "CorrelationId={CorrelationId} RequestId={RequestId} " +
                "RequestType={RequestType} Model={Model} " +
                "InputTokens={InputTokens} OutputTokens={OutputTokens} " +
                "StopReason={StopReason} LatencyMs={LatencyMs}",
                request.CorrelationId, request.RequestId,
                request.RequestType, messagesResponse.Model,
                response.InputTokensUsed, response.OutputTokensUsed,
                messagesResponse.StopReason, sw.ElapsedMilliseconds);

            return response;
        }
        catch (OperationCanceledException)
        {
            throw; // Propagate cancellation to caller.
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(
                ex,
                "Claude adapter: unhandled exception. " +
                "CorrelationId={CorrelationId} RequestId={RequestId} LatencyMs={LatencyMs}",
                request.CorrelationId, request.RequestId, sw.ElapsedMilliseconds);

            return AIResponse.Failed(
                request.RequestId,
                "Anthropic provider encountered an internal error.",
                sw.ElapsedMilliseconds);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Anthropic does not expose a dedicated health-check endpoint.
    /// A lightweight GET to the API root is issued; any HTTP response (including
    /// 4xx auth errors) indicates the server is reachable. Only network-level
    /// failures (DNS, connect timeout) return <see langword="false"/>.
    /// </remarks>
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client   = _httpClientFactory.CreateClient("anthropic");
            // Anthropic returns 404 on GET to the API root — that means the server is up.
            var response = await client.GetAsync("/", cancellationToken);
            return (int)response.StatusCode < 500;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Claude adapter: health check failed. Provider may be unreachable.");
            return false;
        }
    }
}
