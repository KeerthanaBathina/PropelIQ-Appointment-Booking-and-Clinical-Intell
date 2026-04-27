using UPACIP.Service.VectorSearch;

namespace UPACIP.Service.Rag.Models;

/// <summary>
/// A retrieved chunk enriched with LLM-assigned relevance score and final rank position
/// after semantic re-ranking (US_077 AC-3).
/// </summary>
public sealed class RankedChunk
{
    // ── Original retrieval fields (mirrored from RetrievedChunk) ─────────────

    /// <summary>Primary key of the embedding row in the pgvector table.</summary>
    public Guid Id { get; init; }

    /// <summary>Text content of the matched chunk.</summary>
    public required string Content { get; init; }

    /// <summary>Cosine similarity score from pgvector search (AC-2 threshold ≥ 0.75).</summary>
    public float SimilarityScore { get; init; }

    /// <summary>The embedding category (index) that produced this chunk.</summary>
    public EmbeddingCategory Category { get; init; }

    /// <summary>Human-readable source attribution for staff transparency and citations.</summary>
    public required string SourceAttribution { get; init; }

    // ── Re-ranking fields ─────────────────────────────────────────────────────

    /// <summary>
    /// LLM-assigned relevance score in [0, 1] (AC-3).
    /// Equals <see cref="SimilarityScore"/> when LLM re-ranking was unavailable and
    /// the fallback cosine ordering was used.
    /// </summary>
    public float RelevanceScore { get; init; }

    /// <summary>
    /// 1-based final rank position after combining <see cref="RelevanceScore"/> and
    /// <see cref="DomainWeight"/> tiebreaker.
    /// </summary>
    public int FinalRank { get; init; }

    /// <summary>
    /// Category priority weight used as tiebreaker for near-identical relevance scores
    /// (within 0.05 tolerance): MedicalTerminology=1.0, IntakeTemplate=0.9, CodingGuideline=0.8.
    /// </summary>
    public float DomainWeight { get; init; }
}
