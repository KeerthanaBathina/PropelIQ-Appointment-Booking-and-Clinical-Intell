using UPACIP.Service.Rag.Models;
using UPACIP.Service.VectorSearch;

namespace UPACIP.Service.Rag;

/// <summary>
/// Orchestrates top-K vector retrieval across embedding categories using
/// <see cref="IVectorSearchService"/>, enforces the cosine similarity threshold (≥0.75
/// per AIR-R02), and returns a structured <see cref="RetrievalResult"/> (US_077 AC-1, AC-2).
/// </summary>
public interface IRagRetrievalService
{
    /// <summary>
    /// Retrieves the globally top-K most similar chunks across all specified categories in
    /// <paramref name="request"/>.<see cref="RetrievalRequest.TargetCategories"/>.
    /// Category searches run in parallel to meet the &lt;500ms latency target (AIR-R02).
    ///
    /// When no results meet the similarity threshold, returns a result with
    /// <see cref="RetrievalResult.IsGrounded"/> = <see langword="false"/> and
    /// <see cref="RetrievalResult.GroundingStatus"/> = <c>"no-grounding-available"</c>.
    /// </summary>
    Task<RetrievalResult> RetrieveContextAsync(
        RetrievalRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Single-category convenience overload. Equivalent to calling
    /// <see cref="RetrieveContextAsync"/> with a single-element
    /// <see cref="RetrievalRequest.TargetCategories"/> list.
    /// </summary>
    Task<RetrievalResult> RetrieveContextForCategoryAsync(
        EmbeddingCategory category,
        float[] queryEmbedding,
        int topK = 5,
        float similarityThreshold = 0.75f,
        CancellationToken cancellationToken = default);
}
