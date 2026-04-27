namespace UPACIP.Service.Rag.Chunking.Models;

/// <summary>
/// Result produced by <see cref="IDocumentChunkingService.ChunkDocumentAsync"/>
/// (US_076 AC-1, AIR-R01).
///
/// Contains the full ordered list of <see cref="DocumentChunk"/> segments plus
/// aggregate metadata for the source document.
/// </summary>
public sealed class ChunkingResult
{
    /// <summary>Ordered list of chunks (index 0 = first segment of the document).</summary>
    public required IReadOnlyList<DocumentChunk> Chunks { get; init; }

    /// <summary>Total number of chunks produced (== <see cref="Chunks"/>.Count).</summary>
    public required int TotalChunks { get; init; }

    /// <summary>
    /// Sum of BPE token counts across all chunks.
    /// Note: due to overlap this is greater than the token count of the original document.
    /// </summary>
    public required int TotalTokens { get; init; }

    /// <summary>Source document identifier — propagated from the originating request.</summary>
    public required Guid SourceDocumentId { get; init; }

    /// <summary>Embedding domain of the source document.</summary>
    public required VectorSearch.EmbeddingCategory Category { get; init; }
}
