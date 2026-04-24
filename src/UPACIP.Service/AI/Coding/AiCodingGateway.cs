using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using UPACIP.Service.AI;
using UPACIP.Service.AI.ConversationalIntake;
using UPACIP.Service.Coding;

namespace UPACIP.Service.AI.Coding;

/// <summary>
/// Production implementation of <see cref="IAiCodingGateway"/> that calls OpenAI GPT-4o-mini
/// (primary) and Anthropic Claude 3.5 Sonnet (fallback) to map clinical diagnoses to ICD-10-CM
/// codes (US_047, AIR-003, AC-1 through AC-4, AIR-O03, AIR-S01, AIR-S02, AIR-S04).
///
/// Workflow per call:
/// <list type="number">
///   <item>Sanitise each diagnosis text (PII redaction + injection blocking, AIR-S01).</item>
///   <item>Retrieve RAG context from pgvector coding guideline index (AIR-R01–AIR-R05).</item>
///   <item>Build system + user prompts from versioned Liquid templates (AIR-O03 2 000-token budget).</item>
///   <item>Call OpenAI via Polly circuit breaker (5 failures → 30 s open, AIR-O04).</item>
///   <item>Fall back to Anthropic Claude when primary circuit is open or fails.</item>
///   <item>Parse + validate response (ICD-10 format, confidence, justification, AIR-S02, AC-2).</item>
///   <item>Audit-log request and response (no PII, AIR-S04).</item>
/// </list>
///
/// Resilience (Polly 8 legacy API):
///   Circuit breaker: 5 consecutive exceptions → open; 30 s break duration.
///   Retry: 3 attempts with exponential backoff (2 s, 4 s, 8 s) for transient HTTP errors.
/// </summary>
public sealed class AiCodingGateway : IAiCodingGateway
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants (must align with coding-guardrails.json)
    // ─────────────────────────────────────────────────────────────────────────

    private const int    MaxOutputTokens       = 500;
    private const int    FailuresBeforeOpen     = 5;
    private const int    BreakDurationSeconds   = 30;
    private const int    RetryAttempts          = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented               = false,
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Dependencies
    // ─────────────────────────────────────────────────────────────────────────

    private readonly IHttpClientFactory         _httpClientFactory;
    private readonly AiGatewaySettings          _aiSettings;
    private readonly Icd10RagRetriever          _ragRetriever;
    private readonly CodingGuardrailsService    _guardrails;
    private readonly Icd10PromptBuilder         _promptBuilder;
    private readonly Icd10ResponseParser        _responseParser;
    private readonly CptRagRetriever            _cptRagRetriever;
    private readonly CptPromptBuilder           _cptPromptBuilder;
    private readonly CptResponseParser          _cptResponseParser;
    private readonly AiAuditLogger              _auditLogger;
    private readonly ILogger<AiCodingGateway>   _logger;

    // Per-instance Polly circuit breaker (scoped per DI scope — per coding job).
    private readonly AsyncCircuitBreakerPolicy  _circuitBreaker;

    // Per-instance retry policy with exponential back-off.
    private readonly Polly.Retry.AsyncRetryPolicy _retryPolicy;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public AiCodingGateway(
        IHttpClientFactory          httpClientFactory,
        IOptions<AiGatewaySettings> aiSettings,
        Icd10RagRetriever           ragRetriever,
        CodingGuardrailsService     guardrails,
        Icd10PromptBuilder          promptBuilder,
        Icd10ResponseParser         responseParser,
        CptRagRetriever             cptRagRetriever,
        CptPromptBuilder            cptPromptBuilder,
        CptResponseParser           cptResponseParser,
        AiAuditLogger               auditLogger,
        ILogger<AiCodingGateway>    logger)
    {
        _httpClientFactory = httpClientFactory;
        _aiSettings        = aiSettings.Value;
        _ragRetriever      = ragRetriever;
        _guardrails        = guardrails;
        _promptBuilder     = promptBuilder;
        _responseParser    = responseParser;
        _cptRagRetriever   = cptRagRetriever;
        _cptPromptBuilder  = cptPromptBuilder;
        _cptResponseParser = cptResponseParser;
        _auditLogger       = auditLogger;
        _logger            = logger;

        // Circuit breaker: open after 5 consecutive failures, stay open 30 s (AIR-O04).
        _circuitBreaker = Policy
            .Handle<Exception>(ex => ex is not OperationCanceledException)
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: FailuresBeforeOpen,
                durationOfBreak: TimeSpan.FromSeconds(BreakDurationSeconds),
                onBreak: (ex, d) => logger.LogError(ex,
                    "AiCodingGateway: OpenAI circuit OPEN for {Seconds}s.", (int)d.TotalSeconds),
                onReset:    ()   => logger.LogInformation("AiCodingGateway: OpenAI circuit CLOSED."),
                onHalfOpen: ()   => logger.LogInformation("AiCodingGateway: OpenAI circuit HALF-OPEN."));

        // Retry: 3 attempts, exponential back-off — wraps inside the circuit breaker.
        _retryPolicy = Policy
            .Handle<HttpRequestException>()
            .Or<TaskCanceledException>(ex => ex.InnerException is TimeoutException)
            .WaitAndRetryAsync(
                retryCount: RetryAttempts,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (ex, delay, attempt, _) => logger.LogWarning(
                    "AiCodingGateway: retry {Attempt}/{Max} after {Delay}s. Error={Error}",
                    attempt, RetryAttempts, (int)delay.TotalSeconds, ex.Message));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IAiCodingGateway
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiCodingResult>> GenerateCodesAsync(
        IReadOnlyDictionary<Guid, string> diagnosisDescriptions,
        Guid                              patientIdForAudit,
        CancellationToken                 ct = default)
    {
        var correlationId = Guid.NewGuid();
        var sw            = Stopwatch.StartNew();

        if (diagnosisDescriptions.Count == 0)
        {
            _logger.LogDebug("AiCodingGateway: empty diagnosis input; returning empty results. CorrelationId={Id}", correlationId);
            return [];
        }

        // ── Step 1: PII sanitisation (AIR-S01) ───────────────────────────────
        var sanitised = new Dictionary<Guid, string>(diagnosisDescriptions.Count);
        foreach (var (diagId, rawText) in diagnosisDescriptions)
        {
            var clean = _guardrails.SanitiseInput(rawText, correlationId);
            if (clean is null)
            {
                _logger.LogWarning(
                    "AiCodingGateway: diagnosis {DiagId} blocked by guardrails (injection). " +
                    "Will be uncodable. CorrelationId={Id}", diagId, correlationId);
                // Exclude this diagnosis — it will be added as uncodable later.
                continue;
            }
            sanitised[diagId] = clean;
        }

        // All diagnoses blocked — return uncodable for every ID.
        if (sanitised.Count == 0)
        {
            return diagnosisDescriptions.Keys
                .Select(id => new AiCodingResult { DiagnosisId = id, Suggestions = [] })
                .ToList();
        }

        // ── Step 2: RAG retrieval (AIR-R01–AIR-R05) ──────────────────────────
        var ragContext = await _ragRetriever.RetrieveContextAsync(
            sanitised.Values, correlationId, ct);

        // ── Step 3: Build prompts ─────────────────────────────────────────────
        var (systemPrompt, userPrompt) = _promptBuilder.Build(sanitised, ragContext, correlationId);

        // ── Step 4: Call primary (OpenAI) ─────────────────────────────────────
        var diagnosisIds = sanitised.Keys.ToList();
        string? rawJson  = null;
        var provider     = string.Empty;

        _auditLogger.LogRequest(correlationId, "Icd10Mapping", "openai", _aiSettings.OpenAiModel, systemPrompt);

        try
        {
            rawJson = await _circuitBreaker.ExecuteAsync(() =>
                _retryPolicy.ExecuteAsync(() =>
                    CallOpenAiAsync(systemPrompt, userPrompt, correlationId, ct)));

            if (rawJson is not null) provider = "openai";
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning(
                "AiCodingGateway: OpenAI circuit open; falling back to Anthropic. CorrelationId={Id}", correlationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AiCodingGateway: OpenAI call failed; falling back to Anthropic. CorrelationId={Id}", correlationId);
        }

        // ── Step 5: Anthropic fallback ────────────────────────────────────────
        if (rawJson is null)
        {
            _auditLogger.LogRequest(correlationId, "Icd10Mapping", "anthropic", _aiSettings.AnthropicModel, systemPrompt);
            try
            {
                rawJson = await CallAnthropicAsync(systemPrompt, userPrompt, correlationId, ct);
                if (rawJson is not null) provider = "anthropic";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "AiCodingGateway: Anthropic fallback also failed. CorrelationId={Id}", correlationId);
            }
        }

        sw.Stop();
        var success = rawJson is not null;

        _auditLogger.LogResponse(correlationId, "Icd10Mapping", provider, provider == "anthropic"
            ? _aiSettings.AnthropicModel : _aiSettings.OpenAiModel, success, sw.ElapsedMilliseconds);

        // ── Step 6: Parse + validate ──────────────────────────────────────────
        var results = rawJson is not null
            ? _responseParser.Parse(rawJson, diagnosisIds, correlationId)
            : BuildUncodableResults(diagnosisIds);

        // Merge in any diagnoses that were sanitisation-blocked.
        var blockedIds = diagnosisDescriptions.Keys.Except(sanitised.Keys).ToList();
        if (blockedIds.Count > 0)
        {
            var list = results.ToList();
            list.AddRange(blockedIds.Select(id => new AiCodingResult { DiagnosisId = id, Suggestions = [] }));
            results = list;
        }

        _logger.LogInformation(
            "AiCodingGateway: complete. CorrelationId={Id}, Provider={Provider}, " +
            "Diagnoses={Count}, SuccessfulResults={Res}, DurationMs={Ms}",
            correlationId, provider, diagnosisDescriptions.Count, results.Count(r => r.Suggestions.Count > 0), sw.ElapsedMilliseconds);

        return results;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // OpenAI call (GPT-4o-mini, tool calling)
    // ─────────────────────────────────────────────────────────────────────────

    private Task<string?> CallOpenAiAsync(
        string            systemPrompt,
        string            userPrompt,
        Guid              correlationId,
        CancellationToken ct)
        => CallOpenAiWithToolAsync(systemPrompt, userPrompt, GetToolDefinitionJson(), GetToolChoiceJson(), correlationId, ct);

    // ─────────────────────────────────────────────────────────────────────────
    // Anthropic call (Claude 3.5 Sonnet, tool use)
    // ─────────────────────────────────────────────────────────────────────────

    private Task<string?> CallAnthropicAsync(
        string            systemPrompt,
        string            userPrompt,
        Guid              correlationId,
        CancellationToken ct)
        => CallAnthropicWithToolAsync(systemPrompt, userPrompt, GetToolDefinitionJson(), correlationId, ct);

    // ─────────────────────────────────────────────────────────────────────────
    // Response extractors
    // ─────────────────────────────────────────────────────────────────────────

    private string? ExtractOpenAiToolArguments(JsonElement json, Guid correlationId)
    {
        try
        {
            var choice  = json.GetProperty("choices")[0];
            var message = choice.GetProperty("message");
            if (!message.TryGetProperty("tool_calls", out var toolCalls) ||
                toolCalls.GetArrayLength() == 0) return null;

            return toolCalls[0].GetProperty("function").GetProperty("arguments").GetString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AiCodingGateway: failed to extract OpenAI tool arguments. CorrelationId={Id}", correlationId);
            return null;
        }
    }

    private string? ExtractAnthropicToolArguments(JsonElement json, Guid correlationId)
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
                "AiCodingGateway: failed to extract Anthropic tool arguments. CorrelationId={Id}", correlationId);
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<AiCodingResult> BuildUncodableResults(IReadOnlyList<Guid> ids)
        => ids.Select(id => new AiCodingResult { DiagnosisId = id, Suggestions = [] }).ToList();

    // ─────────────────────────────────────────────────────────────────────────
    // IAiCodingGateway — GenerateCptCodesAsync (US_048)
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<AiCptCodingResult>> GenerateCptCodesAsync(
        IReadOnlyDictionary<Guid, string> procedureDescriptions,
        Guid                              patientIdForAudit,
        CancellationToken                 ct = default)
    {
        var correlationId = Guid.NewGuid();
        var sw            = Stopwatch.StartNew();

        if (procedureDescriptions.Count == 0)
        {
            _logger.LogDebug("AiCodingGateway: empty procedure input; returning empty CPT results. CorrelationId={Id}", correlationId);
            return [];
        }

        // ── Step 1: PII sanitisation (AIR-S01) ───────────────────────────────
        var sanitised = new Dictionary<Guid, string>(procedureDescriptions.Count);
        foreach (var (procId, rawText) in procedureDescriptions)
        {
            var clean = _guardrails.SanitiseInput(rawText, correlationId);
            if (clean is null)
            {
                _logger.LogWarning(
                    "AiCodingGateway: procedure {ProcId} blocked by guardrails (injection). " +
                    "Will be uncodable. CorrelationId={Id}", procId, correlationId);
                continue;
            }
            sanitised[procId] = clean;
        }

        if (sanitised.Count == 0)
        {
            return procedureDescriptions.Keys
                .Select(id => new AiCptCodingResult { ProcedureId = id, Suggestions = [] })
                .ToList();
        }

        // ── Step 2: RAG retrieval (AIR-R01–AIR-R05) ──────────────────────────
        var ragContext = await _cptRagRetriever.RetrieveContextAsync(
            sanitised.Values, correlationId, ct);

        // ── Step 3: Build prompts ─────────────────────────────────────────────
        var (systemPrompt, userPrompt) = _cptPromptBuilder.Build(
            sanitised, ragContext, bundleRulesContext: string.Empty, correlationId);

        // ── Step 4: Call primary (OpenAI) ─────────────────────────────────────
        var procedureIds = sanitised.Keys.ToList();
        string? rawJson  = null;
        var provider     = string.Empty;

        _auditLogger.LogRequest(correlationId, "CptMapping", "openai", _aiSettings.OpenAiModel, systemPrompt);

        try
        {
            rawJson = await _circuitBreaker.ExecuteAsync(() =>
                _retryPolicy.ExecuteAsync(() =>
                    CallOpenAiWithToolAsync(systemPrompt, userPrompt, GetCptToolDefinitionJson(), GetCptToolChoiceJson(), correlationId, ct)));

            if (rawJson is not null) provider = "openai";
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning(
                "AiCodingGateway: OpenAI circuit open; falling back to Anthropic (CPT). CorrelationId={Id}", correlationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AiCodingGateway: OpenAI call failed (CPT); falling back to Anthropic. CorrelationId={Id}", correlationId);
        }

        // ── Step 5: Anthropic fallback ────────────────────────────────────────
        if (rawJson is null)
        {
            _auditLogger.LogRequest(correlationId, "CptMapping", "anthropic", _aiSettings.AnthropicModel, systemPrompt);
            try
            {
                rawJson = await CallAnthropicWithToolAsync(systemPrompt, userPrompt, GetCptToolDefinitionJson(), correlationId, ct);
                if (rawJson is not null) provider = "anthropic";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "AiCodingGateway: Anthropic fallback also failed (CPT). CorrelationId={Id}", correlationId);
            }
        }

        sw.Stop();
        var success = rawJson is not null;

        _auditLogger.LogResponse(correlationId, "CptMapping", provider, provider == "anthropic"
            ? _aiSettings.AnthropicModel : _aiSettings.OpenAiModel, success, sw.ElapsedMilliseconds);

        // ── Step 6: Parse + validate ──────────────────────────────────────────
        var results = rawJson is not null
            ? _cptResponseParser.Parse(rawJson, procedureIds, correlationId)
            : BuildCptUncodableResults(procedureIds);

        // Merge in any procedures that were sanitisation-blocked.
        var blockedIds = procedureDescriptions.Keys.Except(sanitised.Keys).ToList();
        if (blockedIds.Count > 0)
        {
            var list = results.ToList();
            list.AddRange(blockedIds.Select(id => new AiCptCodingResult { ProcedureId = id, Suggestions = [] }));
            results = list;
        }

        _logger.LogInformation(
            "AiCodingGateway (CPT): complete. CorrelationId={Id}, Provider={Provider}, " +
            "Procedures={Count}, SuccessfulResults={Res}, DurationMs={Ms}",
            correlationId, provider, procedureDescriptions.Count,
            results.Count(r => r.Suggestions.Count > 0), sw.ElapsedMilliseconds);

        return results;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Generic provider helpers (reusable for CPT tool calls)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<string?> CallOpenAiWithToolAsync(
        string            systemPrompt,
        string            userPrompt,
        string            toolDefinitionJson,
        string            toolChoiceJson,
        Guid              correlationId,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("openai");

        var messages = new[]
        {
            new { role = "system", content = systemPrompt },
            new { role = "user",   content = userPrompt   },
        };

        var requestBody = new
        {
            model       = _aiSettings.OpenAiModel,
            messages,
            tools       = JsonDocument.Parse(toolDefinitionJson).RootElement,
            tool_choice = JsonDocument.Parse(toolChoiceJson).RootElement,
            max_tokens  = MaxOutputTokens,
        };

        using var response = await client.PostAsJsonAsync(
            "/v1/chat/completions", requestBody, JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "AiCodingGateway: OpenAI HTTP {Status}. CorrelationId={Id}",
                (int)response.StatusCode, correlationId);
            throw new HttpRequestException(
                $"OpenAI returned {(int)response.StatusCode}.", null, response.StatusCode);
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return ExtractOpenAiToolArguments(json, correlationId);
    }

    private async Task<string?> CallAnthropicWithToolAsync(
        string            systemPrompt,
        string            userPrompt,
        string            toolDefinitionJson,
        Guid              correlationId,
        CancellationToken ct)
    {
        var client    = _httpClientFactory.CreateClient("anthropic");
        var toolSchema = JsonDocument.Parse(toolDefinitionJson).RootElement;

        var requestBody = new
        {
            model      = _aiSettings.AnthropicModel,
            max_tokens = MaxOutputTokens,
            system     = systemPrompt,
            messages   = new[] { new { role = "user", content = userPrompt } },
            tools      = toolSchema.ValueKind == JsonValueKind.Array
                ? toolSchema.EnumerateArray().ToArray()
                : Array.Empty<JsonElement>(),
            tool_choice = new { type = "auto" },
        };

        using var response = await client.PostAsJsonAsync(
            "/v1/messages", requestBody, JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "AiCodingGateway: Anthropic HTTP {Status}. CorrelationId={Id}",
                (int)response.StatusCode, correlationId);
            throw new HttpRequestException(
                $"Anthropic returned {(int)response.StatusCode}.", null, response.StatusCode);
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return ExtractAnthropicToolArguments(json, correlationId);
    }

    private static IReadOnlyList<AiCptCodingResult> BuildCptUncodableResults(IReadOnlyList<Guid> ids)
        => ids.Select(id => new AiCptCodingResult { ProcedureId = id, Suggestions = [] }).ToList();

    /// <summary>
    /// OpenAI / Anthropic tool definition JSON for the <c>map_icd10_codes</c> function.
    /// Anthropic expects a flat array of tools; OpenAI wraps each in <c>{"type":"function","function":{...}}</c>.
    /// This definition uses the OpenAI format; Anthropic silently ignores the wrapper when <c>type=function</c>.
    /// </summary>
    private static string GetToolDefinitionJson() => """
[
  {
    "type": "function",
    "function": {
      "name": "map_icd10_codes",
      "description": "Maps each clinical diagnosis to the appropriate ICD-10-CM codes with confidence scores and justifications.",
      "parameters": {
        "type": "object",
        "properties": {
          "results": {
            "type": "array",
            "description": "One entry per diagnosis ID in the input.",
            "items": {
              "type": "object",
              "properties": {
                "diagnosis_id": {
                  "type": "string",
                  "description": "The UUID of the diagnosis from the input."
                },
                "codes": {
                  "type": "array",
                  "description": "ICD-10-CM code suggestions ordered by relevance. Empty array when uncodable.",
                  "items": {
                    "type": "object",
                    "properties": {
                      "code_value": {
                        "type": "string",
                        "description": "ICD-10-CM code (e.g. J18.9) or the string 'UNCODABLE'."
                      },
                      "description": {
                        "type": "string",
                        "description": "Official ICD-10-CM description for the code."
                      },
                      "confidence": {
                        "type": "number",
                        "minimum": 0.0,
                        "maximum": 1.0,
                        "description": "Confidence score in [0.0, 1.0]. Use 0.00 for UNCODABLE."
                      },
                      "justification": {
                        "type": "string",
                        "description": "Clinical justification (minimum 20 characters)."
                      },
                      "relevance_rank": {
                        "type": "integer",
                        "description": "Rank starting from 1 (most relevant first)."
                      }
                    },
                    "required": ["code_value", "description", "confidence", "justification", "relevance_rank"]
                  }
                }
              },
              "required": ["diagnosis_id", "codes"]
            }
          }
        },
        "required": ["results"]
      }
    }
  }
]
""";

    private static string GetToolChoiceJson() =>
        """{"type": "function", "function": {"name": "map_icd10_codes"}}""";

    // ─────────────────────────────────────────────────────────────────────────
    // CPT tool definition (US_048, AC-1, AC-3)
    // ─────────────────────────────────────────────────────────────────────────

    private static string GetCptToolDefinitionJson() => """
[
  {
    "type": "function",
    "function": {
      "name": "map_cpt_codes",
      "description": "Maps each clinical procedure to the appropriate CPT codes with confidence scores, justifications, and bundling information.",
      "parameters": {
        "type": "object",
        "properties": {
          "results": {
            "type": "array",
            "description": "One entry per procedure ID in the input.",
            "items": {
              "type": "object",
              "properties": {
                "procedure_id": {
                  "type": "string",
                  "description": "The UUID of the procedure from the input."
                },
                "codes": {
                  "type": "array",
                  "description": "CPT code suggestions ordered by relevance. Empty array when uncodable.",
                  "items": {
                    "type": "object",
                    "properties": {
                      "code_value": {
                        "type": "string",
                        "description": "5-digit CPT code (e.g. '99213') or the string 'UNCODABLE'."
                      },
                      "description": {
                        "type": "string",
                        "description": "AMA CPT description for the code."
                      },
                      "confidence": {
                        "type": "number",
                        "minimum": 0.0,
                        "maximum": 1.0,
                        "description": "Confidence score in [0.0, 1.0]. Use 0.00 for UNCODABLE."
                      },
                      "justification": {
                        "type": "string",
                        "description": "Clinical justification (minimum 20 characters). No PII."
                      },
                      "relevance_rank": {
                        "type": "integer",
                        "description": "Rank starting from 1 (most relevant first). Bundled code = rank 1."
                      },
                      "is_bundled": {
                        "type": "boolean",
                        "description": "True when this code represents a bundled billing unit."
                      },
                      "bundle_components": {
                        "type": "array",
                        "nullable": true,
                        "description": "Component CPT codes included in this bundle (only when is_bundled = true).",
                        "items": { "type": "string" }
                      }
                    },
                    "required": ["code_value", "description", "confidence", "justification", "relevance_rank", "is_bundled"]
                  }
                }
              },
              "required": ["procedure_id", "codes"]
            }
          }
        },
        "required": ["results"]
      }
    }
  }
]
""";

    private static string GetCptToolChoiceJson() =>
        """{"type": "function", "function": {"name": "map_cpt_codes"}}""";
}
