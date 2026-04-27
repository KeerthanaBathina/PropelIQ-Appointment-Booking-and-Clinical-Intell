namespace UPACIP.Service.Rag.Chunking.Models;

/// <summary>
/// A single text segment produced by the sliding-window chunking pipeline
/// (US_076 AC-1, AIR-R01).
///
/// Each chunk is a 512-token (max 520 with boundary alignment) slice of the
/// source document with 102-token overlap with the preceding chunk.  The first
/// chunk always has <see cref="OverlapTokens"/> = 0.
/// </summary>
public sealed class DocumentChunk
{
    /// <summary>0-based position of this chunk within the document.</summary>
    public required int ChunkIndex { get; init; }

    /// <summary>Decoded text content of this chunk after BPE slicing.</summary>
    public required string Content { get; init; }

    /// <summary>Exact BPE token count for this chunk (≤ 520).</summary>
    public required int TokenCount { get; init; }

    /// <summary>
    /// Number of BPE tokens this chunk shares with the immediately preceding chunk.
    /// Always 0 for <see cref="ChunkIndex"/> == 0 (first chunk has no predecessor).
    /// Approximately 102 for all other chunks per AIR-R01.
    /// </summary>
    public required int OverlapTokens { get; init; }

    /// <summary>Source document identifier — propagated from <see cref="Models.ChunkingRequest.SourceDocumentId"/>.</summary>
    public required Guid SourceDocumentId { get; init; }

    /// <summary>Human-readable document name — propagated from <see cref="Models.ChunkingRequest.SourceName"/>.</summary>
    public required string SourceName { get; init; }
}
