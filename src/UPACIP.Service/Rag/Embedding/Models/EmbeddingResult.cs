namespace UPACIP.Service.Rag.Embedding.Models;

/// <summary>
/// Result DTO returned by <see cref="IEmbeddingGenerationService"/> for a single text
/// (US_076 AC-2, AIR-O09 cost tracking).
/// </summary>
public sealed class EmbeddingResult
{
    /// <summary>384-dimensional embedding vector produced by text-embedding-3-small.</summary>
    public required float[] Embedding { get; init; }

    /// <summary>
    /// Total tokens consumed by the OpenAI API for this embedding.
    /// Zero when the result was served from the Redis cache (AIR-O09 cost avoidance).
    /// </summary>
    public int TokensUsed { get; init; }

    /// <summary>
    /// <see langword="true"/> when the embedding was served from the Redis cache
    /// (24-hour TTL keyed by SHA-256 of the text, per AIR-O06).
    /// </summary>
    public bool WasCached { get; init; }
}
