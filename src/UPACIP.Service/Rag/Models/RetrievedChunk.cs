using UPACIP.Service.VectorSearch;

namespace UPACIP.Service.Rag.Models;

/// <summary>
/// A single retrieved knowledge-base chunk returned by <see cref="IRagRetrievalService"/> (US_077 AC-1).
/// </summary>
public sealed class RetrievedChunk
{
    /// <summary>Primary key of the embedding row in the pgvector table.</summary>
    public Guid Id { get; init; }

    /// <summary>Text content of the matched chunk (term, template section, or guideline paragraph).</summary>
    public required string Content { get; init; }

    /// <summary>
    /// Cosine similarity score in [0, 1]. Always ≥ the request threshold (AC-2).
    /// </summary>
    public float SimilarityScore { get; init; }

    /// <summary>The embedding category (index) that produced this chunk.</summary>
    public EmbeddingCategory Category { get; init; }

    /// <summary>
    /// Human-readable source attribution (document name / section) for staff transparency.
    /// Populated from the content column via <see cref="VectorSearch.VectorSearchResult"/>.
    /// </summary>
    public required string SourceAttribution { get; init; }
}
