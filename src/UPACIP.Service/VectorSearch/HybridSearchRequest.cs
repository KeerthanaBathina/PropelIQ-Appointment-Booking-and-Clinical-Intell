namespace UPACIP.Service.VectorSearch;

/// <summary>
/// Input parameters for a hybrid vector + full-text search request (AIR-R06).
/// Results are ranked using Reciprocal Rank Fusion (RRF) over both result lists.
/// </summary>
public sealed class HybridSearchRequest
{
    /// <summary>
    /// 384-dimension query embedding produced by the sentence-transformer model.
    /// Must have exactly 384 elements; a shorter or longer array is rejected with
    /// <see cref="ArgumentException"/>.
    /// </summary>
    public required float[] QueryEmbedding { get; init; }

    /// <summary>
    /// Plain-text search query forwarded to PostgreSQL <c>plainto_tsquery('english', ...)</c>.
    /// May differ from the source text used to generate <see cref="QueryEmbedding"/>.
    /// </summary>
    public required string TextQuery { get; init; }

    /// <summary>
    /// Maximum number of results to return after RRF merging. Valid range: 1–100.
    /// </summary>
    public int TopK { get; init; } = 5;

    /// <summary>
    /// Minimum cosine similarity score (0.0–1.0) for the vector leg.
    /// Vector results below this threshold are excluded before RRF merging.
    /// </summary>
    public float SimilarityThreshold { get; init; } = 0.75f;
}
