using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;

namespace UPACIP.Service.AI.ConversationalIntake;

/// <summary>
/// Core orchestration for the AI conversational intake workflow (AIR-001, FR-026, UC-002).
///
/// Architecture:
///   - Uses OpenAI GPT-4o-mini (primary) via tool calling for structured field extraction.
///   - Falls back to Anthropic Claude 3.5 Sonnet when the primary circuit breaker opens
///     or when model confidence is below the 80% threshold (AIR-010, AIR-O04).
///   - Returns a deterministic "switch to manual" response when both providers fail
///     for <see cref="FallbackConsecutiveFailuresBeforeManual"/> consecutive turns (NFR-022).
///
/// Circuit breaker (AIR-O04):
///   Opens after 5 consecutive exceptions; retries after 30 seconds.
///   State is per-instance (scoped DI), so failures are isolated to the request scope
///   rather than accumulating across sessions (consistent with the NoShowRiskScoringService pattern).
///
/// Token budget (AIR-O02): 500 input tokens / 200 output tokens per exchange.
///   Enforced by <see cref="IntakePromptBuilder"/> (prompt truncation) and the
///   <c>max_tokens</c> parameter in the chat completion request.
///
/// Safety (AIR-S01, AIR-S06):
///   - PII (name, DOB, phone) is never included in structured log messages.
///   - Patient input is sanitised for prompt injection before any model call.
/// </summary>
public sealed class ConversationalIntakeService : IConversationalIntakeService
{
    // Guardrail constants matching guardrails.json
    private const double ConfidenceThreshold = 0.80;
    private const int FallbackConsecutiveFailuresBeforeManual = 3;
    private const int MaxOutputTokens = 200; // AIR-O02

    private const string ProviderOpenAi    = "openai";
    private const string ProviderAnthropic = "anthropic";
    private const string ProviderFallback  = "fallback";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AiGatewaySettings _settings;
    private readonly IntakePromptBuilder _promptBuilder;
    private readonly IntakeFieldExtractionValidator _validator;
    private readonly IntakeRagRetriever _ragRetriever;
    private readonly ILogger<ConversationalIntakeService> _logger;

    // Polly circuit breaker wrapping the OpenAI call path (AIR-O04).
    // Per-instance so breaker state is isolated per DI scope (per request).
    private readonly AsyncCircuitBreakerPolicy _openAiCircuitBreaker;

    public ConversationalIntakeService(
        IHttpClientFactory httpClientFactory,
        IOptions<AiGatewaySettings> settings,
        IntakePromptBuilder promptBuilder,
        IntakeFieldExtractionValidator validator,
        IntakeRagRetriever ragRetriever,
        ILogger<ConversationalIntakeService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings.Value;
        _promptBuilder = promptBuilder;
        _validator = validator;
        _ragRetriever = ragRetriever;
        _logger = logger;

        // Open after 5 consecutive provider exceptions; retry after 30 s (AIR-O04)
        _openAiCircuitBreaker = Policy
            .Handle<Exception>(ex => ex is not OperationCanceledException)
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 5,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (ex, duration) =>
                    _logger.LogError(ex,
                        "ConversationalIntake: OpenAI circuit OPEN for {DurationSeconds}s.",
                        (int)duration.TotalSeconds),
                onReset: () =>
                    _logger.LogInformation("ConversationalIntake: OpenAI circuit CLOSED (reset)."),
                onHalfOpen: () =>
                    _logger.LogInformation("ConversationalIntake: OpenAI circuit HALF-OPEN (probing)."));
    }

    // ─── ProcessMessageAsync (AIR-001, AC-2) ──────────────────────────────────

    public async Task<IntakeExchangeResult> ProcessMessageAsync(
        IntakeSessionContext sessionContext,
        string patientInput,
        CancellationToken ct = default)
    {
        // ── 1. Prompt injection sanitisation (AIR-S06, OWASP A03) ─────────────
        var sanitised = _validator.SanitisePatientInput(patientInput);
        if (sanitised.IsBlocked)
        {
            _logger.LogWarning(
                "ConversationalIntake: prompt injection blocked; sessionId={SessionId}, fieldKey={FieldKey}.",
                sessionContext.SessionId, sessionContext.CurrentFieldKey);
            return BuildInjectionBlockedResult(sessionContext.CurrentFieldKey);
        }

        // ── 2. Enforce max-turns guardrail ────────────────────────────────────
        if (sessionContext.TurnCount >= 30)
        {
            _logger.LogWarning(
                "ConversationalIntake: max turns reached; sessionId={SessionId}.",
                sessionContext.SessionId);
            return BuildManualFallbackResult(sessionContext.CurrentFieldKey,
                "The maximum number of turns has been reached.");
        }

        // ── 3. RAG context retrieval (AIR-R02) ────────────────────────────────
        // Embedding is not available in this service layer — the caller (API controller)
        // may optionally pre-compute and pass it. Here we pass null; the RAG retriever
        // will gracefully skip retrieval and return an empty context string.
        // TODO: wire an embedding service when the Embedding API task is implemented.
        var ragContext = await _ragRetriever.RetrieveContextAsync(
            sanitised.Value,
            sessionContext.CurrentFieldKey,
            queryEmbedding: null,
            ct);

        // ── 4. Build prompt ───────────────────────────────────────────────────
        var systemPrompt = _promptBuilder.BuildSystemPrompt(sessionContext, ragContext);
        var messages = _promptBuilder.BuildMessages(systemPrompt, sessionContext.History, sanitised.Value);

        // ── 5. Try OpenAI (primary) ───────────────────────────────────────────
        ModelExchangeResult? modelResult = null;
        var provider = ProviderFallback;

        try
        {
            modelResult = await _openAiCircuitBreaker.ExecuteAsync(
                () => CallOpenAiAsync(messages, ct));
            provider = ProviderOpenAi;
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning(
                "ConversationalIntake: OpenAI circuit open; falling back to Anthropic. sessionId={SessionId}.",
                sessionContext.SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ConversationalIntake: OpenAI call failed; falling back to Anthropic. sessionId={SessionId}.",
                sessionContext.SessionId);
        }

        // ── 6. Anthropic Claude fallback (AIR-010, NFR-022) ──────────────────
        if (modelResult is null || modelResult.Confidence < ConfidenceThreshold)
        {
            var fallbackReason = modelResult is null ? "provider failure" : $"confidence={modelResult.Confidence:F2}";
            _logger.LogInformation(
                "ConversationalIntake: using Anthropic fallback; reason={Reason}; sessionId={SessionId}.",
                fallbackReason, sessionContext.SessionId);

            try
            {
                var anthropicResult = await CallAnthropicAsync(messages, ct);
                if (anthropicResult is not null)
                {
                    modelResult = anthropicResult;
                    provider = ProviderAnthropic;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ConversationalIntake: Anthropic fallback also failed. sessionId={SessionId}.",
                    sessionContext.SessionId);
            }
        }

        // ── 7. Deterministic manual-form fallback (NFR-022) ──────────────────
        if (modelResult is null)
        {
            var failureCount = sessionContext.ConsecutiveProviderFailures + 1;
            var shouldSwitch = failureCount >= FallbackConsecutiveFailuresBeforeManual;

            _logger.LogWarning(
                "ConversationalIntake: both providers failed; consecutiveFailures={Failures}; switchToManual={Switch}; sessionId={SessionId}.",
                failureCount, shouldSwitch, sessionContext.SessionId);

            return new IntakeExchangeResult
            {
                ReplyToPatient = shouldSwitch
                    ? "I'm having trouble connecting to the AI assistant right now. " +
                      "Your progress has been saved — please switch to the manual form to complete your intake."
                    : "I had trouble processing your response. Could you please try again?",
                FieldKey = sessionContext.CurrentFieldKey,
                ShouldSwitchToManual = shouldSwitch,
                Provider = ProviderFallback,
            };
        }

        // ── 8. Validate extracted field (AIR-010, EC-1) ───────────────────────
        var validation = _validator.ValidateExtractedField(
            sessionContext.CurrentFieldKey,
            modelResult.ExtractedValue,
            modelResult.Confidence);

        var isFieldComplete = modelResult.IsFieldComplete && validation.IsValid;
        var extractedValue = isFieldComplete ? validation.CleanValue : null;

        // Determine next field and summary readiness
        var updatedFields = BuildUpdatedFields(sessionContext.CollectedFields, sessionContext.CurrentFieldKey, extractedValue);
        var nextField = IntakeFieldDefinitions.NextFieldToCollect(updatedFields);
        var isSummaryReady = IntakeFieldDefinitions.AreMandatoryFieldsComplete(updatedFields);

        _logger.LogInformation(
            "ConversationalIntake: exchange complete; sessionId={SessionId}, fieldKey={FieldKey}, " +
            "isComplete={IsComplete}, confidence={Confidence:F2}, provider={Provider}, summaryReady={Summary}.",
            sessionContext.SessionId, sessionContext.CurrentFieldKey,
            isFieldComplete, modelResult.Confidence, provider, isSummaryReady);

        return new IntakeExchangeResult
        {
            ReplyToPatient = modelResult.ReplyToPatient,
            FieldKey = sessionContext.CurrentFieldKey,
            ExtractedValue = extractedValue,
            IsFieldComplete = isFieldComplete,
            NeedsClarification = modelResult.NeedsClarification || validation.RequiresClarification,
            ClarificationExamples = modelResult.ClarificationExamples,
            IsSummaryReady = isSummaryReady,
            ShouldSwitchToManual = false,
            NextFieldKey = nextField,
            Provider = provider,
            Confidence = modelResult.Confidence,
        };
    }

    // ─── GenerateSummaryAsync (AC-4) ──────────────────────────────────────────

    public async Task<IntakeSummaryResult> GenerateSummaryAsync(
        Guid sessionId,
        IReadOnlyDictionary<string, string> collectedFields,
        CancellationToken ct = default)
    {
        var summaryPrompt = _promptBuilder.BuildSummaryPrompt(collectedFields);
        var messages = new List<(string Role, string Content)>
        {
            ("system", summaryPrompt),
            ("user", "Please generate the intake summary for my review."),
        };

        string summaryText;
        try
        {
            var result = await _openAiCircuitBreaker.ExecuteAsync(
                () => CallOpenAiTextAsync(messages, ct));
            summaryText = result ?? BuildFallbackSummaryText(collectedFields);
        }
        catch
        {
            summaryText = BuildFallbackSummaryText(collectedFields);
        }

        var fields = BuildSummaryFields(collectedFields);
        var mandatoryCount = fields.Count(f => f.IsMandatory && !string.IsNullOrWhiteSpace(f.Value));

        _logger.LogInformation(
            "ConversationalIntake: summary generated; sessionId={SessionId}, mandatoryCount={Count}.",
            sessionId, mandatoryCount);

        return new IntakeSummaryResult
        {
            SummaryText = summaryText,
            Fields = fields,
            MandatoryCollectedCount = mandatoryCount,
            MandatoryTotalCount = IntakeFieldDefinitions.MandatoryOrder.Count,
        };
    }

    // ─── OpenAI chat completion (tool calling) ────────────────────────────────

    private async Task<ModelExchangeResult?> CallOpenAiAsync(
        IReadOnlyList<(string Role, string Content)> messages,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("openai");

        var requestBody = new
        {
            model      = _settings.OpenAiModel,
            messages   = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            tools      = JsonDocument.Parse(IntakePromptBuilder.GetToolDefinitionJson()).RootElement,
            tool_choice = JsonDocument.Parse(IntakePromptBuilder.GetToolChoiceJson()).RootElement,
            max_tokens = MaxOutputTokens,
        };

        using var response = await client.PostAsJsonAsync(
            "/v1/chat/completions", requestBody, JsonOptions, ct);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

        return ParseOpenAiToolCallResponse(json);
    }

    private async Task<string?> CallOpenAiTextAsync(
        IReadOnlyList<(string Role, string Content)> messages,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("openai");

        var requestBody = new
        {
            model      = _settings.OpenAiModel,
            messages   = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            max_tokens = MaxOutputTokens * 2,  // summary allows more tokens
        };

        using var response = await client.PostAsJsonAsync(
            "/v1/chat/completions", requestBody, JsonOptions, ct);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

        return json
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();
    }

    private static ModelExchangeResult? ParseOpenAiToolCallResponse(JsonElement json)
    {
        var choice = json.GetProperty("choices")[0];
        var message = choice.GetProperty("message");

        if (!message.TryGetProperty("tool_calls", out var toolCalls) || toolCalls.GetArrayLength() == 0)
            return null;

        var toolCall = toolCalls[0];
        if (!toolCall.TryGetProperty("function", out var func)) return null;

        var argsJson = func.GetProperty("arguments").GetString();
        if (string.IsNullOrWhiteSpace(argsJson)) return null;

        using var doc = JsonDocument.Parse(argsJson);
        var args = doc.RootElement;

        return new ModelExchangeResult
        {
            ExtractedValue        = args.TryGetProperty("extracted_value", out var ev) ? ev.GetString() : null,
            Confidence            = args.TryGetProperty("confidence", out var conf) ? conf.GetDouble() : 0.0,
            NeedsClarification    = args.TryGetProperty("needs_clarification", out var nc) && nc.GetBoolean(),
            ClarificationExamples = args.TryGetProperty("clarification_examples", out var ce)
                ? ce.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList()
                : [],
            ReplyToPatient = args.TryGetProperty("reply_to_patient", out var rtp)
                ? rtp.GetString() ?? string.Empty
                : string.Empty,
            IsFieldComplete = args.TryGetProperty("is_field_complete", out var ifc) && ifc.GetBoolean(),
        };
    }

    // ─── Anthropic Claude fallback (simplified messages API) ─────────────────

    private async Task<ModelExchangeResult?> CallAnthropicAsync(
        IReadOnlyList<(string Role, string Content)> messages,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("anthropic");

        // Separate system message from conversation turns (Anthropic API format)
        var systemContent = messages.FirstOrDefault(m => m.Role == "system").Content ?? string.Empty;
        var conversationMessages = messages
            .Where(m => m.Role != "system")
            .Select(m => new { role = m.Role, content = m.Content })
            .ToArray();

        var requestBody = new
        {
            model      = _settings.AnthropicModel,
            max_tokens = MaxOutputTokens,
            system     = systemContent + "\n\nIMPORTANT: Respond ONLY with valid JSON matching this schema: " +
                         """{"extracted_value":string|null,"confidence":number,"needs_clarification":boolean,"clarification_examples":string[],"reply_to_patient":string,"is_field_complete":boolean}""",
            messages   = conversationMessages,
        };

        using var response = await client.PostAsJsonAsync(
            "/v1/messages", requestBody, JsonOptions, ct);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

        return ParseAnthropicResponse(json);
    }

    private static ModelExchangeResult? ParseAnthropicResponse(JsonElement json)
    {
        var content = json.GetProperty("content")[0];
        var text = content.GetProperty("text").GetString();

        if (string.IsNullOrWhiteSpace(text)) return null;

        // Extract JSON from the response text (model may add surrounding prose)
        var startIdx = text.IndexOf('{');
        var endIdx   = text.LastIndexOf('}');
        if (startIdx < 0 || endIdx <= startIdx) return null;

        var jsonSlice = text[startIdx..(endIdx + 1)];

        try
        {
            using var doc = JsonDocument.Parse(jsonSlice);
            var root = doc.RootElement;

            return new ModelExchangeResult
            {
                ExtractedValue        = root.TryGetProperty("extracted_value", out var ev) ? ev.GetString() : null,
                Confidence            = root.TryGetProperty("confidence", out var conf) ? conf.GetDouble() : 0.0,
                NeedsClarification    = root.TryGetProperty("needs_clarification", out var nc) && nc.GetBoolean(),
                ClarificationExamples = root.TryGetProperty("clarification_examples", out var ce)
                    ? ce.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToList()
                    : [],
                ReplyToPatient = root.TryGetProperty("reply_to_patient", out var rtp)
                    ? rtp.GetString() ?? string.Empty
                    : string.Empty,
                IsFieldComplete = root.TryGetProperty("is_field_complete", out var ifc) && ifc.GetBoolean(),
            };
        }
        catch
        {
            return null;
        }
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private static IntakeExchangeResult BuildInjectionBlockedResult(string fieldKey) =>
        new()
        {
            ReplyToPatient = "I noticed an unusual pattern in your message. Could you please rephrase your response?",
            FieldKey = fieldKey,
            NeedsClarification = true,
            Provider = ProviderFallback,
        };

    private static IntakeExchangeResult BuildManualFallbackResult(string fieldKey, string reason) =>
        new()
        {
            ReplyToPatient = "I'm unable to continue the AI intake session at this time. " +
                             "Your progress has been saved — you can switch to the manual form to complete your intake.",
            FieldKey = fieldKey,
            ShouldSwitchToManual = true,
            Provider = ProviderFallback,
        };

    private static IReadOnlyDictionary<string, string> BuildUpdatedFields(
        IReadOnlyDictionary<string, string> current,
        string fieldKey,
        string? newValue)
    {
        if (string.IsNullOrWhiteSpace(newValue)) return current;

        var updated = new Dictionary<string, string>(current, StringComparer.Ordinal)
        {
            [fieldKey] = newValue,
        };
        return updated;
    }

    private static string BuildFallbackSummaryText(IReadOnlyDictionary<string, string> fields)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Here is a summary of the information collected:");
        sb.AppendLine();
        foreach (var (key, value) in fields)
        {
            var label = IntakeFieldDefinitions.Labels.GetValueOrDefault(key, key);
            sb.AppendLine($"• {label}: {value}");
        }
        sb.AppendLine();
        sb.Append("Please review the information above. Does everything look correct?");
        return sb.ToString();
    }

    private static IReadOnlyList<IntakeSummaryField> BuildSummaryFields(
        IReadOnlyDictionary<string, string> collectedFields)
    {
        var result = new List<IntakeSummaryField>();

        // Mandatory fields first, then optional — in defined order
        foreach (var key in IntakeFieldDefinitions.AllFields)
        {
            if (!collectedFields.TryGetValue(key, out var value)) continue;
            if (string.IsNullOrWhiteSpace(value)) continue;

            result.Add(new IntakeSummaryField
            {
                Key = key,
                Label = IntakeFieldDefinitions.Labels.GetValueOrDefault(key, key),
                Value = value,
                IsMandatory = IntakeFieldDefinitions.IsMandatory(key),
                IsEditable = true,
            });
        }

        return result;
    }

    // ─── Internal model (not exposed outside this service) ───────────────────

    private sealed class ModelExchangeResult
    {
        public string? ExtractedValue        { get; init; }
        public double  Confidence            { get; init; }
        public bool    NeedsClarification    { get; init; }
        public IReadOnlyList<string> ClarificationExamples { get; init; } = [];
        public string  ReplyToPatient        { get; init; } = string.Empty;
        public bool    IsFieldComplete       { get; init; }
    }
}
