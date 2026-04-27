using UPACIP.Service.Rag.Models;

namespace UPACIP.Service.Rag;

/// <summary>
/// Re-ranks retrieved chunks by relevance to the specific query context using
/// LLM-based scoring via the AI Gateway (US_077 AC-3, AIR-R03).
///
/// Domain priority weights resolve ties for ambiguous multi-domain queries (edge case):
/// MedicalTerminology (1.0) > IntakeTemplate (0.9) > CodingGuideline (0.8).
///
/// Falls back to cosine-similarity ordering when the LLM is unavailable,
/// setting <see cref="RerankResult.UsedLlmReranking"/> = <see langword="false"/>.
/// </summary>
public interface ISemanticReranker
{
    /// <summary>
    /// Scores each chunk in <paramref name="chunks"/> for relevance to
    /// <paramref name="queryText"/> using GPT-4o-mini (primary) / Claude 3.5 Sonnet (fallback)
    /// via the AI Gateway, then applies domain priority weighting as a tiebreaker.
    ///
    /// When <paramref name="chunks"/> is empty, returns an empty <see cref="RerankResult"/>
    /// immediately without calling the AI Gateway.
    /// </summary>
    Task<RerankResult> RerankAsync(
        IReadOnlyList<RetrievedChunk> chunks,
        string queryText,
        CancellationToken cancellationToken = default);
}
