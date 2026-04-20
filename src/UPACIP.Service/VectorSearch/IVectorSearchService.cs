namespace UPACIP.Service.VectorSearch;

/// <summary>
/// Abstraction over pgvector-backed semantic search operations for the UPACIP platform.
///
/// All query methods accept a 384-dimension embedding array.  Passing an array of any
/// other length throws <see cref="ArgumentException"/> which the API layer translates
/// to HTTP 400 Bad Request.
/// </summary>
public interface IVectorSearchService
{
    /// <summary>
    /// Returns the top-K most similar embeddings to <paramref name="queryEmbedding"/>
    /// using cosine similarity (<c>&lt;=&gt;</c> operator) against the specified
    /// <paramref name="category"/> table (AIR-R02).
    /// </summary>
    /// <param name="category">Embedding table to search.</param>
    /// <param name="queryEmbedding">384-dimension query vector.</param>
    /// <param name="topK">Maximum number of results. Valid range: 1–100.</param>
    /// <param name="similarityThreshold">
    /// Minimum cosine similarity in [0, 1]. Results below this value are excluded.
    /// </param>
    /// <returns>Results ordered by similarity descending.</returns>
    Task<IReadOnlyList<VectorSearchResult>> SearchSimilarAsync(
        EmbeddingCategory category,
        float[] queryEmbedding,
        int topK = 5,
        float similarityThreshold = 0.75f);

    /// <summary>
    /// Hybrid search combining cosine similarity and PostgreSQL full-text search,
    /// merged using Reciprocal Rank Fusion (RRF, constant = 60) (AIR-R06).
    /// </summary>
    /// <param name="category">Embedding table to search.</param>
    /// <param name="request">Hybrid search parameters (embedding + text query + limits).</param>
    /// <returns>Results ordered by RRF combined score descending.</returns>
    Task<IReadOnlyList<VectorSearchResult>> HybridSearchAsync(
        EmbeddingCategory category,
        HybridSearchRequest request);

    /// <summary>
    /// Inserts a new embedding or updates an existing row by <paramref name="id"/> using
    /// <c>INSERT … ON CONFLICT (id) DO UPDATE</c>.
    /// </summary>
    /// <param name="category">Target embedding table.</param>
    /// <param name="id">Stable UUID for this embedding (must match across upserts).</param>
    /// <param name="content">Main text content used for the embedding.</param>
    /// <param name="embedding">384-dimension embedding vector.</param>
    /// <param name="metadata">
    /// Additional category-specific fields (e.g. description, source, codeSystem).
    /// Keys are case-insensitive. Unknown keys are silently ignored.
    /// </param>
    Task UpsertEmbeddingAsync(
        EmbeddingCategory category,
        Guid id,
        string content,
        float[] embedding,
        Dictionary<string, string>? metadata = null);

    /// <summary>
    /// Removes the embedding row with the given <paramref name="id"/> from the
    /// <paramref name="category"/> table. A no-op if the row does not exist.
    /// </summary>
    Task DeleteEmbeddingAsync(EmbeddingCategory category, Guid id);
}
