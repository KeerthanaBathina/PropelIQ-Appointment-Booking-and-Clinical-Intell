using Microsoft.Extensions.Logging;
using UPACIP.Service.VectorSearch;

namespace UPACIP.Service.AI.ConversationalIntake;

/// <summary>
/// Retrieves grounded medical terminology and intake-flow context from the pgvector
/// knowledge base to augment conversational intake prompts (AIR-R02, AIR-001).
///
/// Retrieval strategy per models.md UC-002 sequence:
///   1. Search <see cref="EmbeddingCategory.MedicalTerminology"/> for any domain-specific
///      terms in the patient's input (cosine ≥ 0.75, top-5).
///   2. Search <see cref="EmbeddingCategory.IntakeTemplate"/> for the intake-flow context
///      relevant to the current field being collected (cosine ≥ 0.75, top-3).
///   3. Merge, de-duplicate, and truncate to <see cref="MaxContextChars"/> for token
///      budget compliance (AIR-O02: 500 input token limit).
///
/// Safety: query text comes from patient input after sanitisation — no raw unsanitised
/// input reaches the embedding lookup (AIR-S06).
/// </summary>
public sealed class IntakeRagRetriever
{
    // Guardrails matching guardrails.json §Rag
    private const int TopKMedical  = 5;
    private const int TopKTemplate = 3;
    private const float SimilarityThreshold = 0.75f;
    private const int MaxContextChars = 400;
    private const int EmbeddingDimensions = 384;

    private readonly IVectorSearchService _vectorSearch;
    private readonly ILogger<IntakeRagRetriever> _logger;

    public IntakeRagRetriever(
        IVectorSearchService vectorSearch,
        ILogger<IntakeRagRetriever> logger)
    {
        _vectorSearch = vectorSearch;
        _logger = logger;
    }

    /// <summary>
    /// Retrieves combined RAG context for a conversational intake exchange.
    /// Returns an empty string when no relevant context is found or retrieval fails.
    /// Never throws — failures are logged and swallowed so the exchange can continue
    /// with an ungrounded prompt rather than failing entirely.
    /// </summary>
    /// <param name="patientInput">Sanitised patient utterance (post AIR-S06 sanitisation).</param>
    /// <param name="currentFieldKey">The field currently being collected.</param>
    /// <param name="queryEmbedding">
    /// 384-dimension embedding of <paramref name="patientInput"/> produced by the caller.
    /// Passing null disables vector retrieval (used in tests and when the embedding service
    /// is unavailable).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Concatenated RAG snippets, truncated to <see cref="MaxContextChars"/>.</returns>
    public async Task<string> RetrieveContextAsync(
        string patientInput,
        string currentFieldKey,
        float[]? queryEmbedding,
        CancellationToken ct = default)
    {
        if (queryEmbedding is null || queryEmbedding.Length != EmbeddingDimensions)
        {
            _logger.LogDebug(
                "IntakeRAG: embedding unavailable for field={FieldKey}; skipping retrieval.",
                currentFieldKey);
            return string.Empty;
        }

        try
        {
            // Run medical-terminology and intake-template searches concurrently (AIR-R02)
            var medicalTask = _vectorSearch.SearchSimilarAsync(
                EmbeddingCategory.MedicalTerminology,
                queryEmbedding,
                topK: TopKMedical,
                similarityThreshold: SimilarityThreshold);

            var templateTask = _vectorSearch.SearchSimilarAsync(
                EmbeddingCategory.IntakeTemplate,
                queryEmbedding,
                topK: TopKTemplate,
                similarityThreshold: SimilarityThreshold);

            await Task.WhenAll(medicalTask, templateTask);

            var medical  = await medicalTask;
            var template = await templateTask;

            _logger.LogDebug(
                "IntakeRAG: field={FieldKey}, medicalHits={MedHits}, templateHits={TplHits}.",
                currentFieldKey, medical.Count, template.Count);

            // Merge and de-duplicate by content, prefer higher similarity
            var merged = medical
                .Concat(template)
                .GroupBy(r => r.Content, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(r => r.Similarity ?? 0).First())
                .OrderByDescending(r => r.CombinedScore ?? r.Similarity ?? 0)
                .ToList();

            // Concatenate and truncate to token-budget-safe size (AIR-O02)
            var sb = new System.Text.StringBuilder(MaxContextChars + 64);
            foreach (var result in merged)
            {
                if (string.IsNullOrWhiteSpace(result.Content)) continue;
                var snippet = result.Content.Trim();
                if (sb.Length + snippet.Length + 2 > MaxContextChars) break;
                sb.Append("- ").AppendLine(snippet);
            }

            return sb.ToString().Trim();
        }
        catch (Exception ex)
        {
            // RAG failure must not block the exchange — log and continue without grounding
            _logger.LogWarning(ex,
                "IntakeRAG: retrieval failed for field={FieldKey}; proceeding without context.",
                currentFieldKey);
            return string.Empty;
        }
    }
}
