namespace UPACIP.Service.Rag.Models;

/// <summary>
/// Aggregated result of a RAG retrieval operation across one or more embedding categories
/// (US_077 AC-1, AC-2, edge case: no-grounding-available).
/// </summary>
public sealed class RetrievalResult
{
    /// <summary>
    /// Top-K chunks sorted by cosine similarity descending.
    /// Empty when <see cref="IsGrounded"/> is <see langword="false"/>.
    /// </summary>
    public required IReadOnlyList<RetrievedChunk> Chunks { get; init; }

    /// <summary>
    /// <see langword="true"/> when at least one chunk exceeded the similarity threshold (AC-2).
    /// <see langword="false"/> triggers the "no-grounding-available" path in the AI Gateway.
    /// </summary>
    public bool IsGrounded { get; init; }

    /// <summary>
    /// Human-readable grounding status. Value is either <c>"grounded"</c> or
    /// <c>"no-grounding-available"</c> (edge case per US_077).
    /// </summary>
    public required string GroundingStatus { get; init; }

    /// <summary>
    /// Total number of raw candidates evaluated across all searched categories
    /// before threshold filtering and top-K selection.
    /// </summary>
    public int TotalCandidatesEvaluated { get; init; }

    /// <summary>
    /// Wall-clock time for the entire retrieval operation (all categories + aggregation).
    /// Used to verify the &lt;500ms latency target per AIR-R02.
    /// </summary>
    public TimeSpan RetrievalLatency { get; init; }
}
