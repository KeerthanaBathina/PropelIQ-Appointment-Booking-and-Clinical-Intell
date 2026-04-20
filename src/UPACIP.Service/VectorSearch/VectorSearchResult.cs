namespace UPACIP.Service.VectorSearch;

/// <summary>
/// Result item returned by <see cref="IVectorSearchService"/> for both
/// cosine similarity and hybrid search operations.
/// </summary>
public sealed class VectorSearchResult
{
    /// <summary>Primary key of the matching embedding row.</summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Human-readable content for the matched row (term, template content, or guideline text
    /// depending on the <see cref="EmbeddingCategory"/> queried).
    /// </summary>
    public string Content { get; init; } = string.Empty;

    /// <summary>
    /// Cosine similarity score in the range [0, 1].
    /// 1.0 = identical vectors; values below the configured threshold are excluded.
    /// Null when the result was sourced from a pure FTS match with no vector counterpart.
    /// </summary>
    public float? Similarity { get; init; }

    /// <summary>
    /// PostgreSQL <c>ts_rank</c> score for the full-text search component.
    /// Null for pure cosine similarity search results.
    /// </summary>
    public float? FtsRank { get; init; }

    /// <summary>
    /// Reciprocal Rank Fusion (RRF) combined score used to rank hybrid search results.
    /// Computed as: <c>(1 / (60 + vec_rank)) + (1 / (60 + text_rank))</c>.
    /// Null for pure cosine similarity search results.
    /// </summary>
    public float? CombinedScore { get; init; }
}
