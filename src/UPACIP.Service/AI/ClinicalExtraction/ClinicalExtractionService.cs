using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.AI.ConversationalIntake;

namespace UPACIP.Service.AI.ClinicalExtraction;

/// <summary>
/// Orchestrates structured clinical data extraction from sanitised document text
/// (US_040 AC-1–AC-5, EC-1, EC-2; AIR-002, AIR-S01, AIR-O01, AIR-O08).
///
/// Workflow:
///   1. Language gate — return <c>unsupported-language</c> before any model call (EC-2).
///   2. PII redaction + injection sanitisation from document content (AIR-S01).
///   3. Token budget enforcement: cap content to <see cref="ClinicalExtractionPromptBuilder.MaxDocumentContentChars"/>.
///   4. Build prompts via <see cref="ClinicalExtractionPromptBuilder"/>.
///   5. Invoke OpenAI GPT-4o-mini (primary) with <c>extract_clinical_data</c> tool calling.
///   6. Anthropic Claude 3.5 Sonnet fallback on circuit-open or primary failure.
///   7. Validate + normalise response via <see cref="ClinicalExtractionResultValidator"/>.
///   8. Return <see cref="ClinicalExtractionResult"/> to the caller.
///
/// Callers are responsible for persisting <see cref="ClinicalExtractionResult.Items"/> to
/// the <c>extracted_data</c> table. This service is deliberately persistence-free.
/// </summary>
public sealed class ClinicalExtractionService
{
    // ── Constants ─────────────────────────────────────────────────────────────────

    private const int MaxOutputTokens = ClinicalExtractionPromptBuilder.MaxOutputTokens;

    // ── PII + injection sanitisation (AIR-S01, AIR-S06) ──────────────────────────

    private static readonly Regex SsnPattern = new(
        @"\b\d{3}-\d{2}-\d{4}\b|\b\d{9}\b",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));

    private static readonly string[] InjectionKeywords =
    [
        "ignore previous instructions", "disregard above", "system prompt",
        "jailbreak", "act as", "you are now", "</system>", "<|im_end|>",
        "DAN mode", "developer mode", "bypass guardrails", "override safety",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ── Dependencies ──────────────────────────────────────────────────────────────

    private readonly ClinicalExtractionPromptBuilder        _promptBuilder;
    private readonly ClinicalExtractionResultValidator      _validator;
    private readonly ClinicalExtractionLanguageGate         _languageGate;
    private readonly IHttpClientFactory                     _httpClientFactory;
    private readonly AiGatewaySettings                      _aiSettings;
    private readonly ILogger<ClinicalExtractionService>     _logger;

    // Per-instance circuit breaker for OpenAI (scoped — per-job isolation).
    private readonly AsyncCircuitBreakerPolicy _openAiCircuitBreaker;

    public ClinicalExtractionService(
        ClinicalExtractionPromptBuilder        promptBuilder,
        ClinicalExtractionResultValidator      validator,
        ClinicalExtractionLanguageGate         languageGate,
        IHttpClientFactory                     httpClientFactory,
        IOptions<AiGatewaySettings>            aiSettings,
        ILogger<ClinicalExtractionService>     logger)
    {
        _promptBuilder     = promptBuilder;
        _validator         = validator;
        _languageGate      = languageGate;
        _httpClientFactory = httpClientFactory;
        _aiSettings        = aiSettings.Value;
        _logger            = logger;

        _openAiCircuitBreaker = Policy
            .Handle<Exception>(ex => ex is not OperationCanceledException)
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 3,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak:    (ex, d)  => _logger.LogError(ex, "ClinicalExtractionService: OpenAI circuit OPEN for {DurationSeconds}s.", (int)d.TotalSeconds),
                onReset:    ()       => _logger.LogInformation("ClinicalExtractionService: OpenAI circuit CLOSED."),
                onHalfOpen: ()       => _logger.LogInformation("ClinicalExtractionService: OpenAI circuit HALF-OPEN."));
    }

    // ─── Main entry point ─────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts structured clinical entities from the sanitised document text.
    /// Never throws — all failures are represented as <see cref="ExtractionOutcome.InvalidResponse"/>.
    /// </summary>
    public async Task<ClinicalExtractionResult> ExtractAsync(
        Guid             documentId,
        DocumentCategory category,
        string           documentText,
        CancellationToken ct)
    {
        // ── Step 1: Language gate (EC-2) ──────────────────────────────────────────
        if (!_languageGate.IsEnglish(documentText, documentId))
        {
            _logger.LogInformation(
                "ClinicalExtractionService: unsupported language detected. DocumentId={DocumentId}",
                documentId);

            return new ClinicalExtractionResult
            {
                Outcome       = ExtractionOutcome.UnsupportedLanguage,
                Confidence    = 0,
                OutcomeReason = "Document language is not supported in Phase 1 (English only).",
                Items         = [],
            };
        }

        // ── Step 2: Sanitise content (AIR-S01, AIR-S06) ──────────────────────────
        var sanitised = SanitiseContent(documentText, documentId);

        // ── Step 3: Build prompts ─────────────────────────────────────────────────
        var messages = _promptBuilder.BuildMessages(documentId, category, sanitised);

        // ── Step 4: Invoke primary model (OpenAI) ────────────────────────────────
        string? rawArguments = null;
        var     provider     = string.Empty;

        try
        {
            rawArguments = await _openAiCircuitBreaker.ExecuteAsync(
                () => CallOpenAiAsync(messages, documentId, ct));
            if (rawArguments is not null) provider = "openai";
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning(
                "ClinicalExtractionService: OpenAI circuit open; falling back to Anthropic. DocumentId={DocumentId}",
                documentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ClinicalExtractionService: OpenAI call failed; falling back to Anthropic. DocumentId={DocumentId}",
                documentId);
        }

        // ── Step 5: Anthropic fallback ────────────────────────────────────────────
        if (rawArguments is null)
        {
            try
            {
                rawArguments = await CallAnthropicAsync(messages, documentId, ct);
                if (rawArguments is not null) provider = "anthropic";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ClinicalExtractionService: Anthropic fallback failed. DocumentId={DocumentId}",
                    documentId);
            }
        }

        if (rawArguments is null)
        {
            _logger.LogError(
                "ClinicalExtractionService: all providers failed. DocumentId={DocumentId}",
                documentId);

            return new ClinicalExtractionResult
            {
                Outcome       = ExtractionOutcome.InvalidResponse,
                Confidence    = 0,
                OutcomeReason = "All AI providers failed to return a response.",
                Items         = [],
            };
        }

        // ── Step 6: Validate + normalise ─────────────────────────────────────────
        var result = _validator.Validate(rawArguments, documentId);

        _logger.LogInformation(
            "ClinicalExtractionService: extraction complete. " +
            "DocumentId={DocumentId} Provider={Provider} Outcome={Outcome} Items={Count}",
            documentId, provider, result.Outcome, result.Items.Count);

        return result;
    }

    // ─── AI provider calls ────────────────────────────────────────────────────────

    private async Task<string?> CallOpenAiAsync(
        IReadOnlyList<(string Role, string Content)> messages,
        Guid             documentId,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("openai");

        var requestBody = new
        {
            model       = _aiSettings.OpenAiModel,
            messages    = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            tools       = JsonDocument.Parse(ClinicalExtractionPromptBuilder.GetToolDefinitionJson()).RootElement,
            tool_choice = JsonDocument.Parse(ClinicalExtractionPromptBuilder.GetToolChoiceJson()).RootElement,
            max_tokens  = MaxOutputTokens,
        };

        using var response = await client.PostAsJsonAsync("/v1/chat/completions", requestBody, JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "ClinicalExtractionService: OpenAI HTTP {Status}. DocumentId={DocumentId}",
                (int)response.StatusCode, documentId);
            throw new HttpRequestException(
                $"OpenAI returned {(int)response.StatusCode}.", null, response.StatusCode);
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return ExtractOpenAiToolArguments(json, documentId);
    }

    private async Task<string?> CallAnthropicAsync(
        IReadOnlyList<(string Role, string Content)> messages,
        Guid             documentId,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("anthropic");

        var systemPrompt = messages.FirstOrDefault(m => m.Role == "system").Content ?? string.Empty;
        var userMessages = messages.Where(m => m.Role != "system")
                                   .Select(m => new { role = m.Role, content = m.Content })
                                   .ToArray();

        var toolSchema = JsonDocument.Parse(ClinicalExtractionPromptBuilder.GetToolDefinitionJson()).RootElement;

        var requestBody = new
        {
            model      = _aiSettings.AnthropicModel,
            max_tokens = MaxOutputTokens,
            system     = systemPrompt,
            messages   = userMessages,
            tools      = toolSchema.ValueKind == JsonValueKind.Array
                ? toolSchema.EnumerateArray().ToArray()
                : Array.Empty<JsonElement>(),
            tool_choice = new { type = "auto" },
        };

        using var response = await client.PostAsJsonAsync("/v1/messages", requestBody, JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "ClinicalExtractionService: Anthropic HTTP {Status}. DocumentId={DocumentId}",
                (int)response.StatusCode, documentId);
            throw new HttpRequestException(
                $"Anthropic returned {(int)response.StatusCode}.", null, response.StatusCode);
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return ExtractAnthropicToolArguments(json, documentId);
    }

    // ─── Response parsers ─────────────────────────────────────────────────────────

    private string? ExtractOpenAiToolArguments(JsonElement json, Guid documentId)
    {
        try
        {
            var choices = json.GetProperty("choices");
            if (choices.GetArrayLength() == 0) return null;

            var message   = choices[0].GetProperty("message");
            var toolCalls = message.GetProperty("tool_calls");
            if (toolCalls.GetArrayLength() == 0) return null;

            return toolCalls[0].GetProperty("function").GetProperty("arguments").GetString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ClinicalExtractionService: failed to parse OpenAI response. DocumentId={DocumentId}",
                documentId);
            return null;
        }
    }

    private string? ExtractAnthropicToolArguments(JsonElement json, Guid documentId)
    {
        try
        {
            foreach (var block in json.GetProperty("content").EnumerateArray())
            {
                if (block.TryGetProperty("type", out var t) &&
                    t.GetString() == "tool_use" &&
                    block.TryGetProperty("input", out var input))
                {
                    return input.GetRawText();
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ClinicalExtractionService: failed to parse Anthropic response. DocumentId={DocumentId}",
                documentId);
            return null;
        }
    }

    // ─── Sanitisation helpers ─────────────────────────────────────────────────────

    private string SanitiseContent(string text, Guid documentId)
    {
        // Truncate to token budget (AIR-O01).
        if (text.Length > ClinicalExtractionPromptBuilder.MaxDocumentContentChars)
            text = text[..ClinicalExtractionPromptBuilder.MaxDocumentContentChars];

        // Strip SSN-like patterns (AIR-S01).
        text = SsnPattern.Replace(text, "[REDACTED]");

        // Block injection keywords (AIR-S06, OWASP A03).
        bool injected = false;
        foreach (var kw in InjectionKeywords)
        {
            if (text.Contains(kw, StringComparison.OrdinalIgnoreCase))
            {
                text     = text.Replace(kw, "[BLOCKED]", StringComparison.OrdinalIgnoreCase);
                injected = true;
            }
        }

        if (injected)
        {
            _logger.LogWarning(
                "ClinicalExtractionService: injection keyword detected in document. DocumentId={DocumentId}",
                documentId);
        }

        return text;
    }
}
