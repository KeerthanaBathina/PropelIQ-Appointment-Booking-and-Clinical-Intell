using UPACIP.Service.VectorSearch;

namespace UPACIP.Service.Rag.Embedding.Models;

/// <summary>
/// Request DTO for single-text embedding generation (US_076 AC-2, AIR-R04).
/// </summary>
public sealed class EmbeddingRequest
{
    /// <summary>Text content to embed. Must not be null or whitespace.</summary>
    public required string Text { get; init; }

    /// <summary>
    /// Embedding category that determines the target pgvector table (AIR-R04 separate indexes).
    /// </summary>
    public EmbeddingCategory Category { get; init; }

    /// <summary>
    /// Optional source document identifier used for traceability in audit logs (AIR-S04).
    /// Never serialised into log messages to protect content confidentiality.
    /// </summary>
    public Guid? SourceDocumentId { get; init; }
}
