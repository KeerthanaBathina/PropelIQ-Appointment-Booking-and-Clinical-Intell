using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using UPACIP.Service.AI.ConversationalIntake;
using UPACIP.Service.Consolidation;
using UPACIP.Service.VectorSearch;

namespace UPACIP.Service.AI.ConflictDetection;

/// <summary>
/// AI-powered clinical conflict detection service (US_043, AC-1, AIR-005, AIR-S09, AIR-S10, AIR-Q07).
///
/// Workflow per analysis request:
///   1. Sanitise input — PII redaction + injection keyword blocking (AIR-S01, AIR-S06).
///   2. Token budget — truncate data payload to <see cref="MaxDataPointChars"/> (AIR-O01).
///   3. RAG context — attempt to generate a query embedding (OpenAI text-embedding-3-small)
///      and retrieve top-5 medical terminology chunks from pgvector (AIR-R02).
///      If embedding generation fails, context is left empty and analysis proceeds without it.
///   4. Build prompt — load <c>conflict-detection.liquid</c> and interpolate variables.
///   5. Primary model — call OpenAI GPT-4o-mini with <c>analyze_clinical_conflicts</c> tool
///      through the Polly circuit breaker (5 consecutive failures → 30 s open, AIR-O04).
///   6. Anthropic fallback — Claude 3.5 Sonnet when primary fails (AIR-O04).
///   7. Validate output — parse JSON tool-call response; on malformed response return
///      <see cref="ConflictAnalysisResult.Empty"/> so consolidation never blocks.
///   8. Audit log — structured log of analysis metadata; NO PII in log events (AIR-S04).
///
/// Circuit breaker (AIR-O04):
///   Per-instance (scoped) Polly circuit breaker; wraps only the OpenAI primary call.
///   Opens after 5 consecutive exceptions, resets after 30 seconds.
///
/// Token budget (AIR-O01):
///   Input: max 4000 tokens (~<see cref="MaxDataPointChars"/> chars for data + prompt overhead).
///   Output: max 1000 tokens (<see cref="MaxOutputTokens"/>).
///
/// PII safety (AIR-S01):
///   SSN-like patterns are scrubbed from normalized values before they reach the API.
///   Patient name and email are never included — only the opaque PatientId UUID is used
///   as a correlation key in audit logs.
/// </summary>
public sealed class ConflictDetectionService : IConflictDetectionService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    private const int MaxOutputTokens   = 1_000;
    private const int MaxDataPointChars = 10_000;  // guardrails.json § TokenBudget
    private const int MaxRagContextChars = 1_500;
    private const int TopKRag           = 5;
    private const float RagSimilarityThreshold = 0.75f;
    private const float MinReportableConfidence = 0.50f;

    /// <summary>
    /// Analysis confidence threshold below which the entire batch is flagged for manual staff
    /// verification (AC-4, AIR-010, AIR-Q07). Applied to the overall analysis confidence score
    /// returned by the LLM, not to individual conflict confidence values.
    /// </summary>
    private const float LowConfidenceThreshold = 0.80f;

    private const int EmbeddingDimensions = 384;   // matches pgvector table dimension

    // Circuit-breaker settings (AIR-O04)
    private const int FailuresBeforeOpen    = 5;
    private const int BreakDurationSeconds  = 30;

    // ─────────────────────────────────────────────────────────────────────────
    // Sanitisation
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly Regex SsnPattern = new(
        @"\b\d{3}-\d{2}-\d{4}\b|\b\d{9}\b",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));

    private static readonly string[] InjectionKeywords =
    [
        "ignore previous instructions", "disregard above", "system prompt",
        "jailbreak", "act as", "you are now", "</system>", "<|im_end|>",
        "DAN mode", "developer mode", "bypass guardrails", "override safety",
    ];

    // ─────────────────────────────────────────────────────────────────────────
    // JSON options
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented               = false,
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Dependencies
    // ─────────────────────────────────────────────────────────────────────────

    private readonly IHttpClientFactory                     _httpClientFactory;
    private readonly IVectorSearchService                   _vectorSearch;
    private readonly AiGatewaySettings                      _aiSettings;
    private readonly ILogger<ConflictDetectionService>      _logger;

    // Per-instance circuit breaker for the OpenAI primary call (scoped — per-job isolation).
    private readonly AsyncCircuitBreakerPolicy _openAiCircuitBreaker;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public ConflictDetectionService(
        IHttpClientFactory                  httpClientFactory,
        IVectorSearchService                vectorSearch,
        IOptions<AiGatewaySettings>         aiSettings,
        ILogger<ConflictDetectionService>   logger)
    {
        _httpClientFactory = httpClientFactory;
        _vectorSearch      = vectorSearch;
        _aiSettings        = aiSettings.Value;
        _logger            = logger;

        // Circuit breaker: open after 5 consecutive failures, stay open 30 s (AIR-O04).
        _openAiCircuitBreaker = Policy
            .Handle<Exception>(ex => ex is not OperationCanceledException)
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: FailuresBeforeOpen,
                durationOfBreak: TimeSpan.FromSeconds(BreakDurationSeconds),
                onBreak:    (ex, d)  => _logger.LogError(ex, "ConflictDetectionSvc: OpenAI circuit OPEN for {DurationSeconds}s.", (int)d.TotalSeconds),
                onReset:    ()       => _logger.LogInformation("ConflictDetectionSvc: OpenAI circuit CLOSED."),
                onHalfOpen: ()       => _logger.LogInformation("ConflictDetectionSvc: OpenAI circuit HALF-OPEN."));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IConflictDetectionService
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ConflictAnalysisResult> DetectConflictsAsync(
        IReadOnlyList<MergedDataPoint> mergedDataPoints,
        Guid                           patientId,
        CancellationToken              ct = default)
    {
        var sw        = Stopwatch.StartNew();
        var analysisId = Guid.NewGuid();

        _logger.LogInformation(
            "ConflictDetectionSvc: analysis started. AnalysisId={AnalysisId}, DataPoints={Count}, PatientId={PatientId}",
            analysisId, mergedDataPoints.Count, patientId);

        // ── Step 1: Nothing to analyse ──────────────────────────────────────
        if (mergedDataPoints.Count == 0)
        {
            _logger.LogInformation(
                "ConflictDetectionSvc: no data points — skipping. AnalysisId={AnalysisId}", analysisId);
            return ConflictAnalysisResult.Empty(sw.ElapsedMilliseconds);
        }

        // ── Step 2: Sanitise + build data payload (AIR-S01, AIR-O01) ──────────
        var existingPoints = mergedDataPoints.Where(p => p.WasPreexisting).ToList();
        var newPoints      = mergedDataPoints.Where(p => !p.WasPreexisting).ToList();

        var existingJson = BuildSanitisedDataJson(existingPoints, patientId);
        var newJson      = BuildSanitisedDataJson(newPoints, patientId);

        // ── Step 2b: Conflict-type routing — select specialised prompt template ──
        // Choose the template that best matches the dominant clinical data type in the
        // incoming batch. If 3+ distinct source documents are present, the multi-source
        // template handles cross-document grouping for the 3+ doc edge case.
        var templateName = SelectPromptTemplateName(mergedDataPoints);

        _logger.LogDebug(
            "ConflictDetectionSvc: routing to template '{Template}'. AnalysisId={AnalysisId}",
            templateName, analysisId);

        // ── Step 3: RAG context retrieval (AIR-R02) ───────────────────────────
        var ragContext = await TryRetrieveRagContextAsync(mergedDataPoints, analysisId, ct);

        // ── Step 4: Build prompt ───────────────────────────────────────────────
        var systemPrompt = BuildSystemPrompt(
            analysisId:      analysisId,
            patientId:       patientId,
            existingJson:    existingJson,
            newJson:         newJson,
            ragContext:      ragContext,
            dataPointCount:  mergedDataPoints.Count,
            templateName:    templateName,
            sourceDocumentCount: mergedDataPoints.Select(p => p.SourceDocumentId).Distinct().Count());

        var messages = new[] { (Role: "system", Content: systemPrompt) };

        // ── Step 5: Call primary model (OpenAI GPT-4o-mini) ──────────────────
        string? rawArguments = null;
        var     provider     = string.Empty;

        try
        {
            rawArguments = await _openAiCircuitBreaker.ExecuteAsync(
                () => CallOpenAiAsync(messages, analysisId, ct));
            if (rawArguments is not null) provider = "openai";
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning(
                "ConflictDetectionSvc: OpenAI circuit open; falling back to Anthropic. AnalysisId={AnalysisId}",
                analysisId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ConflictDetectionSvc: OpenAI call failed; falling back to Anthropic. AnalysisId={AnalysisId}",
                analysisId);
        }

        // ── Step 6: Anthropic fallback ─────────────────────────────────────────
        if (rawArguments is null)
        {
            try
            {
                rawArguments = await CallAnthropicAsync(messages, analysisId, ct);
                if (rawArguments is not null) provider = "anthropic";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "ConflictDetectionSvc: Anthropic fallback failed. AnalysisId={AnalysisId}",
                    analysisId);
            }
        }

        if (rawArguments is null)
        {
            _logger.LogError(
                "ConflictDetectionSvc: all providers failed — returning empty result. AnalysisId={AnalysisId}",
                analysisId);
            sw.Stop();
            return ConflictAnalysisResult.Empty(sw.ElapsedMilliseconds);
        }

        // ── Step 7: Validate + normalise response ──────────────────────────────
        var result = ParseAndValidateResponse(rawArguments, provider, analysisId, sw);

        // ── Step 8: Structured audit log (AIR-S04 — no PII) ───────────────────
        _logger.LogInformation(
            "ConflictDetectionSvc: analysis complete. AnalysisId={AnalysisId}, Provider={Provider}, " +
            "ConflictsFound={Count}, Critical={Critical}, Confidence={Confidence:F2}, DurationMs={Duration}",
            analysisId, result.Provider, result.ConflictCount, result.CriticalCount,
            result.AnalysisConfidence, result.DurationMs);

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RAG context retrieval (AIR-R02)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to:
    ///   1. Generate a 384-dim embedding of the concatenated data point values
    ///      via the OpenAI text-embedding-3-small endpoint.
    ///   2. Query pgvector MedicalTerminology for top-5 chunks (cos sim ≥ 0.75).
    ///   3. Return a truncated context string.
    ///
    /// Returns an empty string when either step fails (graceful degradation).
    /// </summary>
    private async Task<string> TryRetrieveRagContextAsync(
        IReadOnlyList<MergedDataPoint> dataPoints,
        Guid                           analysisId,
        CancellationToken              ct)
    {
        try
        {
            // Build a compact text query from normalized values (medications + diagnoses).
            var queryText = string.Join("; ", dataPoints
                .Where(p => p.NormalizedValue is not null)
                .Select(p => p.NormalizedValue!)
                .Take(20)); // guard against prompt injection via large data sets

            if (string.IsNullOrWhiteSpace(queryText)) return string.Empty;

            // Generate embedding (text-embedding-3-small, dimensions=384 for pgvector compat).
            var embedding = await GenerateEmbeddingAsync(queryText, analysisId, ct);
            if (embedding is null) return string.Empty;

            // Query medical terminology knowledge base.
            var results = await _vectorSearch.SearchSimilarAsync(
                EmbeddingCategory.MedicalTerminology,
                embedding,
                topK: TopKRag,
                similarityThreshold: RagSimilarityThreshold);

            if (results.Count == 0) return string.Empty;

            // Concatenate and truncate to token budget.
            var context = new StringBuilder();
            foreach (var r in results)
            {
                context.Append("- ").AppendLine(r.Content);
                if (context.Length >= MaxRagContextChars) break;
            }

            var contextStr = context.ToString();
            return contextStr.Length > MaxRagContextChars
                ? contextStr[..MaxRagContextChars]
                : contextStr;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ConflictDetectionSvc: RAG retrieval failed; continuing without context. AnalysisId={AnalysisId}",
                analysisId);
            return string.Empty;
        }
    }

    /// <summary>
    /// Calls OpenAI's <c>/v1/embeddings</c> endpoint to generate a 384-dimension query embedding
    /// (text-embedding-3-small with dimension reduction).
    /// Returns null on any failure.
    /// </summary>
    private async Task<float[]?> GenerateEmbeddingAsync(
        string            text,
        Guid              analysisId,
        CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("openai");

            var request = new
            {
                model      = "text-embedding-3-small",
                input      = text,
                dimensions = EmbeddingDimensions,
            };

            using var response = await client.PostAsJsonAsync("/v1/embeddings", request, JsonOptions, ct);

            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);

            var data = json.GetProperty("data");
            if (data.GetArrayLength() == 0) return null;

            var embeddingArray = data[0].GetProperty("embedding");
            var floats = new float[embeddingArray.GetArrayLength()];
            int i = 0;
            foreach (var v in embeddingArray.EnumerateArray())
                floats[i++] = v.GetSingle();

            return floats.Length == EmbeddingDimensions ? floats : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "ConflictDetectionSvc: embedding generation failed. AnalysisId={AnalysisId}", analysisId);
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Prompt construction
    // ─────────────────────────────────────────────────────────────────────────

    private string BuildSystemPrompt(
        Guid   analysisId,
        Guid   patientId,
        string existingJson,
        string newJson,
        string ragContext,
        int    dataPointCount,
        string templateName        = "conflict-detection",
        int    sourceDocumentCount = 0)
    {
        // Load the routed prompt template from disk (output directory pattern).
        var templatePath = Path.Combine(
            AppContext.BaseDirectory,
            "AI", "ConflictDetection", "Prompts", $"{templateName}.liquid");

        string template;
        if (File.Exists(templatePath))
        {
            template = File.ReadAllText(templatePath);
        }
        else
        {
            // Inline fallback for test/dev environments where assets are not copied.
            template = BuildInlinePromptTemplate();
            _logger.LogDebug(
                "ConflictDetectionSvc: prompt template not found at {Path}; using inline default.",
                templatePath);
        }

        // Simple variable substitution — no full Liquid engine dependency required.
        return template
            .Replace("{{ analysis_id }}",           analysisId.ToString())
            .Replace("{{ patient_id_masked }}",     $"***{patientId.ToString()[^4..]}") // last 4 chars only
            .Replace("{{ timestamp }}",             DateTime.UtcNow.ToString("o"))
            .Replace("{{ data_point_count }}",      dataPointCount.ToString())
            .Replace("{{ source_document_count }}", sourceDocumentCount.ToString())
            .Replace("{{ rag_context }}",           ragContext)
            .Replace("{{ existing_data_json }}",    existingJson)
            .Replace("{{ new_data_json }}",         newJson);
    }

    private string BuildSanitisedDataJson(List<MergedDataPoint> points, Guid patientId)
    {
        if (points.Count == 0) return "[]";

        var payload = points.Select(p => new
        {
            extracted_data_id  = p.ExtractedDataId,
            data_type          = p.DataType.ToString(),
            normalized_value   = SanitiseText(p.NormalizedValue),
            raw_text           = SanitiseText(p.RawText),
            confidence_score   = p.ConfidenceScore,
            source_document_id = p.SourceDocumentId,
        }).ToList();

        var json = JsonSerializer.Serialize(payload, JsonOptions);

        // Token budget: truncate if over limit (AIR-O01).
        return json.Length > MaxDataPointChars ? json[..MaxDataPointChars] : json;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AI provider calls
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<string?> CallOpenAiAsync(
        IEnumerable<(string Role, string Content)> messages,
        Guid              analysisId,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("openai");

        var requestBody = new
        {
            model       = _aiSettings.OpenAiModel,
            messages    = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            tools       = JsonDocument.Parse(GetToolDefinitionJson()).RootElement,
            tool_choice = JsonDocument.Parse(GetToolChoiceJson()).RootElement,
            max_tokens  = MaxOutputTokens,
        };

        using var response = await client.PostAsJsonAsync("/v1/chat/completions", requestBody, JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "ConflictDetectionSvc: OpenAI HTTP {Status}. AnalysisId={AnalysisId}",
                (int)response.StatusCode, analysisId);
            throw new HttpRequestException(
                $"OpenAI returned {(int)response.StatusCode}.", null, response.StatusCode);
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return ExtractOpenAiToolArguments(json, analysisId);
    }

    private async Task<string?> CallAnthropicAsync(
        IEnumerable<(string Role, string Content)> messages,
        Guid              analysisId,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("anthropic");

        var msgList = messages.ToList();
        var systemPrompt  = msgList.FirstOrDefault(m => m.Role == "system").Content ?? string.Empty;
        var userMessages  = msgList.Where(m => m.Role != "system")
                                   .Select(m => new { role = m.Role, content = m.Content })
                                   .ToArray();

        // When no user messages exist, add a trigger message so Anthropic processes the system prompt.
        if (userMessages.Length == 0)
        {
            userMessages = [new { role = "user", content = "Analyze the data points above." }];
        }

        var toolSchema = JsonDocument.Parse(GetToolDefinitionJson()).RootElement;

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
                "ConflictDetectionSvc: Anthropic HTTP {Status}. AnalysisId={AnalysisId}",
                (int)response.StatusCode, analysisId);
            throw new HttpRequestException(
                $"Anthropic returned {(int)response.StatusCode}.", null, response.StatusCode);
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return ExtractAnthropicToolArguments(json, analysisId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Response parsers
    // ─────────────────────────────────────────────────────────────────────────

    private string? ExtractOpenAiToolArguments(JsonElement json, Guid analysisId)
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
                "ConflictDetectionSvc: failed to parse OpenAI response. AnalysisId={AnalysisId}",
                analysisId);
            return null;
        }
    }

    private string? ExtractAnthropicToolArguments(JsonElement json, Guid analysisId)
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
                "ConflictDetectionSvc: failed to parse Anthropic response. AnalysisId={AnalysisId}",
                analysisId);
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Output schema validation (AIR-Q07)
    // ─────────────────────────────────────────────────────────────────────────

    private ConflictAnalysisResult ParseAndValidateResponse(
        string  rawArguments,
        string  provider,
        Guid    analysisId,
        Stopwatch sw)
    {
        try
        {
            var doc = JsonDocument.Parse(rawArguments);
            var root = doc.RootElement;

            var conflictsDetected = root.TryGetProperty("conflicts_detected", out var cd)
                && cd.GetBoolean();

            // ── Confidence calibration (AC-4, AIR-Q07) ──────────────────────
            // Normalise to [0, 1]; guard against LLM returning 0-100 scale.
            float rawAnalysisConfidence = root.TryGetProperty("confidence", out var cf)
                ? (float)cf.GetDouble()
                : 0f;
            float analysisConfidence = CalibrateConfidence(rawAnalysisConfidence);

            // Flag entire batch for manual verification when confidence < 0.80 (AC-4, AIR-010).
            bool requiresManualVerification = analysisConfidence < LowConfidenceThreshold;
            if (requiresManualVerification)
            {
                _logger.LogWarning(
                    "ConflictDetectionSvc: overall confidence {Confidence:F2} below manual-review threshold {Threshold:F2}. " +
                    "Entire batch flagged for manual verification. AnalysisId={AnalysisId}",
                    analysisConfidence, LowConfidenceThreshold, analysisId);
            }

            var conflicts = new List<DetectedConflict>();

            if (root.TryGetProperty("conflicts", out var arr) &&
                arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    float rawItemConfidence = item.TryGetProperty("confidence", out var ic)
                        ? (float)ic.GetDouble()
                        : 0f;
                    float itemConfidence = CalibrateConfidence(rawItemConfidence);

                    // Filter out conflicts below minimum reportable confidence (AIR-Q07).
                    if (itemConfidence < MinReportableConfidence) continue;

                    var conflictType = item.TryGetProperty("conflict_type", out var ct) ? ct.GetString() ?? string.Empty : string.Empty;
                    var severityStr  = item.TryGetProperty("severity", out var sv) ? sv.GetString() ?? "Low" : "Low";
                    var severity     = Enum.TryParse<ConflictSeverity>(severityStr, ignoreCase: true, out var sev) ? sev : ConflictSeverity.Low;
                    var reasoning    = item.TryGetProperty("reasoning", out var rs) ? rs.GetString() ?? string.Empty : string.Empty;

                    Guid dataPointAId = item.TryGetProperty("data_point_a_id", out var idA)
                        && Guid.TryParse(idA.GetString(), out var ga) ? ga : Guid.Empty;
                    Guid dataPointBId = item.TryGetProperty("data_point_b_id", out var idB)
                        && Guid.TryParse(idB.GetString(), out var gb) ? gb : Guid.Empty;

                    bool urgentReview = item.TryGetProperty("requires_urgent_review", out var ur)
                        && ur.GetBoolean();

                    // Override urgentReview for all Critical severity conflicts (AIR-S09).
                    if (severity == ConflictSeverity.Critical) urgentReview = true;

                    // Parse multi-source additional IDs (AIR-007, Edge Case — 3+ documents).
                    var additionalIds = new List<Guid>();
                    if (item.TryGetProperty("additional_source_ids", out var extraArr) &&
                        extraArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var extra in extraArr.EnumerateArray())
                        {
                            if (Guid.TryParse(extra.GetString(), out var extraGuid))
                                additionalIds.Add(extraGuid);
                        }
                    }

                    // Sanitise reasoning field — remove injection-prone text (AIR-S06).
                    reasoning = SanitiseText(reasoning) ?? string.Empty;

                    conflicts.Add(new DetectedConflict
                    {
                        ConflictType         = conflictType,
                        Severity             = severity,
                        DataPointAId         = dataPointAId,
                        DataPointBId         = dataPointBId,
                        AdditionalSourceIds  = additionalIds,
                        Reasoning            = reasoning,
                        RequiresUrgentReview = urgentReview,
                        Confidence           = itemConfidence,
                    });
                }
            }

            // Sort: Critical first, then High, Medium, Low (for staff dashboard ordering).
            conflicts.Sort((a, b) => a.Severity.CompareTo(b.Severity));

            sw.Stop();

            return new ConflictAnalysisResult
            {
                ConflictsDetected          = conflictsDetected && conflicts.Count > 0,
                ConflictCount              = conflicts.Count,
                CriticalCount              = conflicts.Count(c => c.Severity == ConflictSeverity.Critical),
                AnalysisConfidence         = analysisConfidence,
                Conflicts                  = conflicts,
                Provider                   = provider,
                DurationMs                 = sw.ElapsedMilliseconds,
                IsFallback                 = false,
                RequiresManualVerification = requiresManualVerification,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ConflictDetectionSvc: output schema validation failed; returning empty result. AnalysisId={AnalysisId}",
                analysisId);
            sw.Stop();
            return ConflictAnalysisResult.Empty(sw.ElapsedMilliseconds);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tool definition (OpenAI function-calling schema)
    // ─────────────────────────────────────────────────────────────────────────

    private static string GetToolDefinitionJson() => """
[
  {
    "type": "function",
    "function": {
      "name": "analyze_clinical_conflicts",
      "description": "Analyzes extracted clinical data points for contradictions, contraindications, and implausible events. Returns a structured list of detected conflicts with severity classification.",
      "parameters": {
        "type": "object",
        "properties": {
          "conflicts_detected": {
            "type": "boolean",
            "description": "True when at least one conflict with confidence >= 0.50 is detected."
          },
          "conflict_count": {
            "type": "integer",
            "description": "Total number of conflicts detected."
          },
          "confidence": {
            "type": "number",
            "description": "Overall analysis confidence score in range 0.0-1.0."
          },
          "conflicts": {
            "type": "array",
            "items": {
              "type": "object",
              "properties": {
                "conflict_type": {
                  "type": "string",
                  "enum": ["MedicationContraindication", "MedicationDiscrepancy", "ConflictingDiagnosis", "DuplicateDiagnosis", "DateInconsistency", "Duplicate"]
                },
                "severity": {
                  "type": "string",
                  "enum": ["Critical", "High", "Medium", "Low"]
                },
                "data_point_a_id": {
                  "type": "string",
                  "description": "UUID of the primary extracted data row involved."
                },
                "data_point_b_id": {
                  "type": "string",
                  "description": "UUID of the secondary extracted data row that conflicts. Use empty string for single-row issues."
                },
                "additional_source_ids": {
                  "type": "array",
                  "items": { "type": "string" },
                  "description": "UUIDs of all further extracted data rows when 3 or more source documents contribute to the same conflict (Edge Case). Empty array for pairwise conflicts."
                },
                "reasoning": {
                  "type": "string",
                  "description": "Plain-English explanation. Use only clinical entity names — NO patient PII."
                },
                "requires_urgent_review": {
                  "type": "boolean",
                  "description": "Set to true for Critical severity conflicts requiring immediate staff review."
                },
                "confidence": {
                  "type": "number",
                  "description": "Confidence in this specific conflict in range 0.0-1.0. Do not report if below 0.50."
                }
              },
              "required": ["conflict_type", "severity", "data_point_a_id", "data_point_b_id", "reasoning", "requires_urgent_review", "confidence"]
            }
          }
        },
        "required": ["conflicts_detected", "conflict_count", "confidence", "conflicts"]
      }
    }
  }
]
""";

    private static string GetToolChoiceJson() => """
{"type": "function", "function": {"name": "analyze_clinical_conflicts"}}
""";

    // ─────────────────────────────────────────────────────────────────────────
    // Conflict-type routing (task_004, AC-1, AC-2, AC-5, Edge Case)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Selects the specialised prompt template that best matches the clinical data types
    /// present in the incoming batch.
    ///
    /// Selection logic (in priority order):
    /// <list type="number">
    ///   <item>3+ distinct source documents → <c>multi-source-conflict</c> (Edge Case).</item>
    ///   <item>Dominant type is <see cref="DataAccess.Enums.DataType.Medication"/> → <c>medication-discrepancy</c> (AC-1, AIR-S09).</item>
    ///   <item>Dominant type is <see cref="DataAccess.Enums.DataType.Diagnosis"/> → <c>duplicate-diagnosis</c> (AC-2).</item>
    ///   <item>Date-bearing types (Procedure, Allergy) or mixed types → <c>date-plausibility</c> (AC-5).</item>
    ///   <item>Fallback → <c>conflict-detection</c> (general template).</item>
    /// </list>
    /// </summary>
    private static string SelectPromptTemplateName(IReadOnlyList<MergedDataPoint> dataPoints)
    {
        // Priority 1: multi-source template when 3+ distinct source documents are present.
        var distinctDocCount = dataPoints.Select(p => p.SourceDocumentId).Distinct().Count();
        if (distinctDocCount >= 3)
            return "multi-source-conflict";

        if (dataPoints.Count == 0)
            return "conflict-detection";

        // Count by data type to find dominant category.
        var typeCounts = dataPoints
            .GroupBy(p => p.DataType)
            .ToDictionary(g => g.Key, g => g.Count());

        var dominantType = typeCounts.OrderByDescending(kv => kv.Value).First().Key;

        return dominantType switch
        {
            DataAccess.Enums.DataType.Medication => "medication-discrepancy",
            DataAccess.Enums.DataType.Diagnosis  => "duplicate-diagnosis",
            DataAccess.Enums.DataType.Procedure  => "date-plausibility",
            _                                    => "conflict-detection",
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Confidence score calibration (AC-4, AIR-Q07)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Normalises an LLM-returned confidence value to the [0.0, 1.0] range (AIR-Q07).
    ///
    /// Guards against two common LLM output errors:
    /// <list type="bullet">
    ///   <item>0-100 scale (e.g., "85" instead of "0.85") — divided by 100.</item>
    ///   <item>Out-of-range values — clamped to [0, 1].</item>
    /// </list>
    /// </summary>
    private static float CalibrateConfidence(float raw)
    {
        // Detect 0-100 scale: any value > 1.0 is assumed to be a percentage.
        if (raw > 1.0f) raw /= 100f;

        return Math.Clamp(raw, 0f, 1f);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Sanitisation helpers (AIR-S01, AIR-S06)
    // ─────────────────────────────────────────────────────────────────────────

    private string? SanitiseText(string? text)
    {
        if (text is null) return null;

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
            _logger.LogWarning("ConflictDetectionSvc: injection keyword blocked in data payload.");
        }

        return text;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Inline prompt fallback (used when liquid file is not found on disk)
    // ─────────────────────────────────────────────────────────────────────────

    private static string BuildInlinePromptTemplate() => """
You are a clinical conflict detection specialist for UPACIP Medical Clinic (US_043, AIR-005).

Analysis ID: {{ analysis_id }}. Patient ID: {{ patient_id_masked }}. Timestamp: {{ timestamp }}.
Total data points: {{ data_point_count }}.

{% if rag_context != "" %}
## Medical Reference Context
{{ rag_context }}
{% endif %}

## Existing Profile Data
{{ existing_data_json }}

## New Data Points
{{ new_data_json }}

Identify medication contraindications (Critical), conflicting diagnoses (High),
chronologically implausible events (Medium), and near-duplicates (Low).
Call analyze_clinical_conflicts exactly once.
Do NOT include patient PII in reasoning fields.
""";
}
