using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using UPACIP.Service.AI.ConversationalIntake;
using UPACIP.Service.Rag.Models;
using UPACIP.Service.VectorSearch;

namespace UPACIP.Service.Rag;

/// <summary>
/// Scoped LLM-based implementation of <see cref="ISemanticReranker"/> (US_077 AC-3, AIR-R03).
///
/// Pipeline:
/// <list type="number">
///   <item>Load re-ranking prompt template (lazy, file-based with inline fallback).</item>
///   <item>Sanitize query text — strip null bytes, truncate to token budget (AIR-O01 / AIR-S04).</item>
///   <item>Send prompt to GPT-4o-mini via "openai" named <see cref="HttpClient"/> (primary).</item>
///   <item>On failure, fall back to "anthropic" client (Claude 3.5 Sonnet) (AIR-R03).</item>
///   <item>Parse JSON array <c>[{"index": N, "relevance": X.XX}]</c>; validate 0–1 range.</item>
///   <item>Apply domain priority weight as tiebreaker; compute final score and assign ranks.</item>
///   <item>On any AI provider failure, fall back to cosine-similarity ordering (no LLM call).</item>
/// </list>
///
/// Token budget (AIR-O01): 500 input tokens ≈ 2000 chars; chunk content section capped at
/// <see cref="MaxChunkContentChars"/> (1400 chars total for all chunks).
/// Query is capped at <see cref="MaxQueryChars"/> (200 chars).
///
/// PII guardrail (AIR-S04): query text is never written to structured log lines; only
/// chunk count, latency, and LLM usage flags are logged.
/// </summary>
public sealed class SemanticReranker : ISemanticReranker
{
    // ── Token budget constants (AIR-O01) ──────────────────────────────────────

    private const int MaxQueryChars        = 200;
    private const int MaxChunkContentChars = 1_400;  // distributed across all chunks
    private const int MaxOutputTokens      = 200;

    // ── Domain priority weights (ambiguous query tiebreaker) ──────────────────

    private const float WeightMedicalTerminology = 1.0f;
    private const float WeightIntakeTemplate      = 0.9f;
    private const float WeightCodingGuideline     = 0.8f;

    // ── Prompt template ───────────────────────────────────────────────────────

    private const string TemplateRelativePath =
        "Rag/Prompts/reranking-prompt.liquid";

    private static readonly object TemplateLock = new();
    private string? _promptTemplate;

    // ── JSON serialization ────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive    = true,
        DefaultIgnoreCondition         = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly IHttpClientFactory             _httpClientFactory;
    private readonly AiGatewaySettings              _settings;
    private readonly ILogger<SemanticReranker>      _logger;

    /// <summary>
    /// Per-instance circuit breaker for OpenAI (scoped lifetime — per request isolation,
    /// matching DocumentParsingWorker pattern). Opens after 3 consecutive failures (AIR-O04).
    /// </summary>
    private readonly AsyncCircuitBreakerPolicy _openAiCircuitBreaker;

    // ── Constructor ───────────────────────────────────────────────────────────

    public SemanticReranker(
        IHttpClientFactory          httpClientFactory,
        IOptions<AiGatewaySettings> settings,
        ILogger<SemanticReranker>   logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings          = settings.Value;
        _logger            = logger;

        _openAiCircuitBreaker = Policy
            .Handle<Exception>(ex => ex is not OperationCanceledException)
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 3,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (ex, duration) =>
                    _logger.LogError(ex,
                        "SemanticReranker: OpenAI circuit OPEN for {DurationSeconds}s.",
                        (int)duration.TotalSeconds),
                onReset: () =>
                    _logger.LogInformation("SemanticReranker: OpenAI circuit CLOSED."),
                onHalfOpen: () =>
                    _logger.LogInformation("SemanticReranker: OpenAI circuit HALF-OPEN."));
    }

    // ── ISemanticReranker ─────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<RerankResult> RerankAsync(
        IReadOnlyList<RetrievedChunk> chunks,
        string queryText,
        CancellationToken cancellationToken = default)
    {
        if (chunks.Count == 0)
        {
            return new RerankResult
            {
                Chunks           = [],
                RerankLatency    = TimeSpan.Zero,
                UsedLlmReranking = false,
            };
        }

        var sw = Stopwatch.StartNew();

        // ── Sanitize inputs (AIR-S04 / OWASP A03) ────────────────────────────
        var safeQuery = SanitizeAndTruncate(queryText, MaxQueryChars);

        // ── Attempt LLM re-ranking ────────────────────────────────────────────
        float[]? llmScores = null;
        var usedLlm = false;

        try
        {
            llmScores = await ScoreWithLlmAsync(safeQuery, chunks, cancellationToken);
            usedLlm = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                "LLM re-ranking unavailable, falling back to cosine similarity ordering. " +
                "ChunkCount={Count} Reason={Message}",
                chunks.Count, ex.Message);
        }

        // ── Build ranked chunks ───────────────────────────────────────────────
        var ranked = BuildRankedChunks(chunks, llmScores);

        sw.Stop();

        _logger.LogInformation(
            "SemanticReranker: {Count} chunks re-ranked in {LatencyMs}ms. UsedLlm={UsedLlm}",
            ranked.Count, sw.ElapsedMilliseconds, usedLlm);

        return new RerankResult
        {
            Chunks           = ranked,
            RerankLatency    = sw.Elapsed,
            UsedLlmReranking = usedLlm,
        };
    }

    // ── Private: LLM scoring ──────────────────────────────────────────────────

    /// <summary>
    /// Sends the scoring prompt to GPT-4o-mini (primary) with a Claude fallback,
    /// and parses the JSON array response.
    /// Returns an array of relevance scores indexed by original chunk order.
    /// </summary>
    private async Task<float[]> ScoreWithLlmAsync(
        string safeQuery,
        IReadOnlyList<RetrievedChunk> chunks,
        CancellationToken ct)
    {
        var (systemPrompt, userPrompt) = BuildPrompt(safeQuery, chunks);

        // ── Primary: OpenAI GPT-4o-mini ───────────────────────────────────────
        string? rawJson = null;

        try
        {
            rawJson = await _openAiCircuitBreaker.ExecuteAsync(async () =>
            {
                var client = _httpClientFactory.CreateClient("openai");
                var body   = new
                {
                    model      = "gpt-4o-mini",
                    messages   = new[]
                    {
                        new { role = "system", content = systemPrompt },
                        new { role = "user",   content = userPrompt   },
                    },
                    max_tokens = MaxOutputTokens,
                };

                using var response = await client.PostAsJsonAsync(
                    "/v1/chat/completions", body, JsonOptions, ct);

                response.EnsureSuccessStatusCode();

                var json     = await response.Content.ReadFromJsonAsync<JsonElement>(
                    cancellationToken: ct);
                return ExtractTextContent(json);
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "SemanticReranker: OpenAI failed, trying Anthropic fallback.");
        }

        // ── Fallback: Anthropic Claude 3.5 Sonnet ─────────────────────────────
        if (rawJson is null)
        {
            var client = _httpClientFactory.CreateClient("anthropic");
            var body   = new
            {
                model      = _settings.AnthropicModel,
                max_tokens = MaxOutputTokens,
                system     = systemPrompt,
                messages   = new[] { new { role = "user", content = userPrompt } },
            };

            using var response = await client.PostAsJsonAsync(
                "/v1/messages", body, JsonOptions, ct);

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(
                cancellationToken: ct);
            rawJson = ExtractAnthropicContent(json);
        }

        return ParseRelevanceScores(rawJson!, chunks.Count);
    }

    // ── Private: prompt building ──────────────────────────────────────────────

    private (string SystemPrompt, string UserPrompt) BuildPrompt(
        string safeQuery,
        IReadOnlyList<RetrievedChunk> chunks)
    {
        var template  = LoadPromptTemplate();
        var chunkBudgetPerChunk = Math.Max(50, MaxChunkContentChars / Math.Max(chunks.Count, 1));

        var chunkLines = new StringBuilder();
        for (var i = 0; i < chunks.Count; i++)
        {
            var truncated = SanitizeAndTruncate(chunks[i].Content, chunkBudgetPerChunk);
            chunkLines.AppendLine($"[{i}] {truncated}");
        }

        var userPrompt = template
            .Replace("{{ query }}",  safeQuery)
            .Replace("{{ chunks }}", chunkLines.ToString().TrimEnd());

        return (GetSystemPrompt(), userPrompt);
    }

    private static string GetSystemPrompt() =>
        "You are a medical knowledge relevance scorer. " +
        "Score only based on direct relevance to the query. " +
        "Do not invent information. Respond only with valid JSON.";

    private string LoadPromptTemplate()
    {
        if (_promptTemplate is not null) return _promptTemplate;

        lock (TemplateLock)
        {
            if (_promptTemplate is not null) return _promptTemplate;

            var path = Path.Combine(
                AppContext.BaseDirectory,
                TemplateRelativePath.Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(path))
            {
                _promptTemplate = File.ReadAllText(path);
            }
            else
            {
                _logger.LogWarning(
                    "SemanticReranker: template not found at {Path}; using inline default.",
                    path);
                _promptTemplate = GetInlineTemplate();
            }

            return _promptTemplate;
        }
    }

    private static string GetInlineTemplate() =>
        """
Score each candidate chunk for relevance to the medical query below.
Respond ONLY with a JSON array, e.g.: [{"index":0,"relevance":0.92},{"index":1,"relevance":0.41}]
No prose. No markdown fences. Scores must be between 0.0 and 1.0.

Query: {{ query }}

Candidate chunks:
{{ chunks }}
""";

    // ── Private: response parsing ─────────────────────────────────────────────

    private static string? ExtractTextContent(JsonElement json)
    {
        try
        {
            return json
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractAnthropicContent(JsonElement json)
    {
        try
        {
            return json
                .GetProperty("content")[0]
                .GetProperty("text")
                .GetString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parses <c>[{"index": N, "relevance": X.XX}]</c> from the LLM response.
    /// Strips markdown fences if the model wrapped the JSON.
    /// Returns an array indexed by original chunk position; missing indexes fall back to 0.
    /// </summary>
    private float[] ParseRelevanceScores(string rawJson, int chunkCount)
    {
        var scores = new float[chunkCount];

        try
        {
            // Strip markdown fences if the model wrapped the JSON (guardrail).
            var trimmed = rawJson.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                var start = trimmed.IndexOf('[');
                var end   = trimmed.LastIndexOf(']');
                if (start >= 0 && end > start)
                    trimmed = trimmed[start..(end + 1)];
            }

            var doc = JsonDocument.Parse(trimmed);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var idx       = item.GetProperty("index").GetInt32();
                var relevance = item.GetProperty("relevance").GetSingle();

                // Validate score range (guardrail: reject out-of-range values).
                if (idx >= 0 && idx < chunkCount && relevance is >= 0f and <= 1f)
                    scores[idx] = relevance;
                else
                    _logger.LogWarning(
                        "SemanticReranker: invalid score entry (index={Index}, relevance={Score}) ignored.",
                        idx, relevance);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "SemanticReranker: failed to parse LLM relevance scores. Scores default to 0.");
        }

        return scores;
    }

    // ── Private: ranking assembly ─────────────────────────────────────────────

    /// <summary>
    /// Combines LLM scores (or falls back to similarity scores), applies domain weights,
    /// sorts, and assigns 1-based <see cref="RankedChunk.FinalRank"/> values.
    /// </summary>
    private static IReadOnlyList<RankedChunk> BuildRankedChunks(
        IReadOnlyList<RetrievedChunk> chunks,
        float[]? llmScores)
    {
        var staged = new (RetrievedChunk Chunk, float RelevanceScore, float DomainWeight)[chunks.Count];

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk   = chunks[i];
            var rel     = llmScores is not null ? llmScores[i] : chunk.SimilarityScore;
            var weight  = GetDomainWeight(chunk.Category);
            staged[i]   = (chunk, rel, weight);
        }

        // Sort: primary = relevance + (domainWeight * 0.01) descending.
        var sorted = staged
            .OrderByDescending(x => x.RelevanceScore + (x.DomainWeight * 0.01f))
            .ToArray();

        var result = new RankedChunk[sorted.Length];
        for (var rank = 0; rank < sorted.Length; rank++)
        {
            var (chunk, rel, weight) = sorted[rank];
            result[rank] = new RankedChunk
            {
                Id               = chunk.Id,
                Content          = chunk.Content,
                SimilarityScore  = chunk.SimilarityScore,
                Category         = chunk.Category,
                SourceAttribution = chunk.SourceAttribution,
                RelevanceScore   = rel,
                FinalRank        = rank + 1,
                DomainWeight     = weight,
            };
        }

        return result;
    }

    private static float GetDomainWeight(EmbeddingCategory category) => category switch
    {
        EmbeddingCategory.MedicalTerminology => WeightMedicalTerminology,
        EmbeddingCategory.IntakeTemplate     => WeightIntakeTemplate,
        EmbeddingCategory.CodingGuideline    => WeightCodingGuideline,
        _                                    => 0.8f,
    };

    // ── Private: sanitization ─────────────────────────────────────────────────

    /// <summary>
    /// Removes null bytes (OWASP A03 injection prevention) and truncates to
    /// <paramref name="maxChars"/> to respect the token budget (AIR-O01).
    /// </summary>
    private static string SanitizeAndTruncate(string text, int maxChars)
    {
        var clean = text.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();
        return clean.Length > maxChars ? clean[..maxChars] : clean;
    }
}
