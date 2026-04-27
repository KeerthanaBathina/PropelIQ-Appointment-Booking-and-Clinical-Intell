namespace UPACIP.Service.Rag.Chunking.Models;

/// <summary>
/// Request DTO for the document chunking pipeline (US_076 AC-1, AIR-R01).
///
/// Carries the raw document text plus traceability metadata used to populate
/// each <see cref="DocumentChunk"/> produced by
/// <see cref="IDocumentChunkingService.ChunkDocumentAsync"/>.
/// </summary>
public sealed class ChunkingRequest
{
    /// <summary>Raw document content before any preprocessing.</summary>
    public required string DocumentText { get; init; }

    /// <summary>
    /// Stable identifier of the source document in the knowledge base.
    /// Propagated to every chunk for downstream traceability.
    /// </summary>
    public required Guid SourceDocumentId { get; init; }

    /// <summary>Human-readable document name / title (e.g. filename or document heading).</summary>
    public required string SourceName { get; init; }

    /// <summary>
    /// Embedding domain this document belongs to.
    /// Reuses the <see cref="VectorSearch.EmbeddingCategory"/> enum from US_009.
    /// </summary>
    public required VectorSearch.EmbeddingCategory Category { get; init; }
}
