using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UPACIP.Service.VectorSearch;

namespace UPACIP.Service.AI.Coding;

/// <summary>
/// Retrieves ICD-10 coding guideline chunks from the pgvector
/// <c>coding_guideline_embeddings</c> index to ground AI code mapping (US_047, AIR-R01–AIR-R05).
///
/// Workflow:
/// <list type="number">
///   <item>Generate a 384-dim embedding of the concatenated diagnosis texts using
///         OpenAI <c>text-embedding-3-small</c>.</item>
///   <item>Query the <c>CodingGuideline</c> pgvector table using cosine similarity
///         (top-5 chunks, threshold ≥ 0.75, AIR-R02).</item>
///   <item>Concatenate and truncate to the guardrail-configured character budget.</item>
/// </list>
///
/// Graceful degradation: any failure at step 1 or 2 returns an empty string so the
/// coding pipeline continues without RAG context rather than blocking the request.
/// </summary>
public sealed class Icd10RagRetriever
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants (mirrored in coding-guardrails.json)
    // ─────────────────────────────────────────────────────────────────────────

    private const int   TopKRag                = 5;
    private const float RagSimilarityThreshold = 0.75f;
    private const int   EmbeddingDimensions    = 384;
    private const int   MaxRagContextChars     = 1_500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Dependencies
    // ─────────────────────────────────────────────────────────────────────────

    private readonly IHttpClientFactory         _httpClientFactory;
    private readonly IVectorSearchService       _vectorSearch;
    private readonly ILogger<Icd10RagRetriever> _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public Icd10RagRetriever(
        IHttpClientFactory         httpClientFactory,
        IVectorSearchService       vectorSearch,
        ILogger<Icd10RagRetriever> logger)
    {
        _httpClientFactory = httpClientFactory;
        _vectorSearch      = vectorSearch;
        _logger            = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Retrieves ICD-10 coding guideline chunks relevant to the given diagnosis texts.
    ///
    /// Returns an empty string on any failure to preserve pipeline availability.
    /// </summary>
    /// <param name="diagnosisTexts">
    /// Collection of diagnosis strings to use as the retrieval query.
    /// Up to 20 entries are used (injection protection).
    /// </param>
    /// <param name="correlationId">Correlation ID for structured logging (NFR-035).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<string> RetrieveContextAsync(
        IEnumerable<string> diagnosisTexts,
        Guid                correlationId,
        CancellationToken   ct = default)
    {
        try
        {
            var queryText = string.Join("; ", diagnosisTexts
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Take(20)); // injection protection

            if (string.IsNullOrWhiteSpace(queryText)) return string.Empty;

            var embedding = await GenerateEmbeddingAsync(queryText, correlationId, ct);
            if (embedding is null) return string.Empty;

            var results = await _vectorSearch.SearchSimilarAsync(
                EmbeddingCategory.CodingGuideline,
                embedding,
                topK: TopKRag,
                similarityThreshold: RagSimilarityThreshold);

            if (results.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            foreach (var r in results)
            {
                sb.Append("- ").AppendLine(r.Content);
                if (sb.Length >= MaxRagContextChars) break;
            }

            var ctx = sb.ToString();
            return ctx.Length > MaxRagContextChars ? ctx[..MaxRagContextChars] : ctx;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Icd10RagRetriever: RAG retrieval failed; continuing without context. " +
                "CorrelationId={CorrelationId}", correlationId);
            return string.Empty;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Embedding generation
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<float[]?> GenerateEmbeddingAsync(
        string            text,
        Guid              correlationId,
        CancellationToken ct)
    {
        try
        {
            var client  = _httpClientFactory.CreateClient("openai");
            var request = new { model = "text-embedding-3-small", input = text, dimensions = EmbeddingDimensions };

            using var response = await client.PostAsJsonAsync("/v1/embeddings", request, JsonOptions, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            var data = json.GetProperty("data");
            if (data.GetArrayLength() == 0) return null;

            var arr    = data[0].GetProperty("embedding");
            var floats = new float[arr.GetArrayLength()];
            var i      = 0;
            foreach (var v in arr.EnumerateArray())
                floats[i++] = v.GetSingle();

            return floats.Length == EmbeddingDimensions ? floats : null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "Icd10RagRetriever: embedding generation failed. CorrelationId={CorrelationId}",
                correlationId);
            return null;
        }
    }
}
