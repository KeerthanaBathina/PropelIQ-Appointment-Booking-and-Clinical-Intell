using UPACIP.Service.Rag.Chunking.Models;

namespace UPACIP.Service.Rag.Chunking;

/// <summary>
/// Splits knowledge base documents into fixed-size BPE token windows with
/// configurable overlap for downstream embedding and RAG retrieval (US_076 AC-1, AIR-R01).
///
/// Token window: 512 tokens, step: 410 tokens (102-token / ~20% overlap).
/// Documents shorter than 100 tokens are returned as a single chunk.
/// </summary>
public interface IDocumentChunkingService
{
    /// <summary>
    /// Preprocesses and chunks <paramref name="request"/>.DocumentText into
    /// 512-token BPE segments.
    /// </summary>
    /// <param name="request">Document text and traceability metadata.</param>
    /// <param name="cancellationToken">Propagated to any async operations.</param>
    /// <returns>
    /// A <see cref="ChunkingResult"/> containing the ordered chunk list plus
    /// aggregate metadata.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="request"/>.DocumentText is null or empty.
    /// </exception>
    Task<ChunkingResult> ChunkDocumentAsync(
        ChunkingRequest request,
        CancellationToken cancellationToken = default);
}
