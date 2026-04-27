using UPACIP.Service.Rag.Models;
using UPACIP.Service.VectorSearch;

namespace UPACIP.Service.Rag;

/// <summary>
/// Orchestrates hybrid (vector + full-text) search across one or more embedding category
/// indexes with configurable weighting, deduplication, and exact-match boosting (US_078 AC-1, AC-2, AC-4).
/// </summary>
public interface IHybridSearchOrchestrator
{
    /// <summary>
    /// Executes hybrid search across the specified (or all) embedding categories.
    ///
    /// <list type="bullet">
    ///   <item>When <paramref name="targetCategories"/> is null or empty, all three indexes
    ///   (MedicalTerminology, IntakeTemplate, CodingGuideline) are queried in parallel (AC-4).</item>
    ///   <item>A single category restricts the query to that index only (AC-4 scoped search).</item>
    ///   <item>Duplicate chunks (same <c>Id</c>) across categories are deduplicated; the entry
    ///   with the higher combined score is kept (AC-2).</item>
    ///   <item>Weighted scoring: <c>FinalScore = (Similarity × SemanticWeight) + (NormalizedFtsRank × KeywordWeight)</c> (AC-1).</item>
    ///   <item>Exact-match boosting: chunks containing a whole-word case-insensitive match of
    ///   <paramref name="textQuery"/> have their score multiplied by <c>ExactMatchBoostFactor</c>
    ///   (edge case: exact ICD-10/CPT code match).</item>
    /// </list>
    /// </summary>
    Task<IReadOnlyList<RetrievedChunk>> HybridSearchAsync(
        float[] queryEmbedding,
        string textQuery,
        IReadOnlyList<EmbeddingCategory>? targetCategories = null,
        int topK = 5,
        float similarityThreshold = 0.75f,
        CancellationToken cancellationToken = default);
}
