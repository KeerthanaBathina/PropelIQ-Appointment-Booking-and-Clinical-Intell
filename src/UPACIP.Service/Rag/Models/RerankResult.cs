namespace UPACIP.Service.Rag.Models;

/// <summary>
/// Result of semantic re-ranking over retrieved chunks (US_077 AC-3).
/// </summary>
public sealed class RerankResult
{
    /// <summary>
    /// Re-ranked chunks sorted by combined relevance + domain-weight score descending.
    /// Empty when the input retrieval contained no chunks.
    /// </summary>
    public required IReadOnlyList<RankedChunk> Chunks { get; init; }

    /// <summary>Wall-clock time for the re-ranking operation.</summary>
    public TimeSpan RerankLatency { get; init; }

    /// <summary>
    /// <see langword="true"/> when LLM-based scoring succeeded.
    /// <see langword="false"/> indicates the fallback cosine-similarity ordering was used
    /// (AI Gateway unavailable or response parsing failed).
    /// </summary>
    public bool UsedLlmReranking { get; init; }
}
