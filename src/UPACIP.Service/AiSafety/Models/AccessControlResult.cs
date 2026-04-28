using UPACIP.Service.Rag.Models;

namespace UPACIP.Service.AiSafety.Models;

/// <summary>
/// Result of a RAG access control filtering operation (US_079 task_002, AIR-S07, AC-2).
///
/// <para>
/// Contains the subset of retrieved chunks that the requesting user is authorized
/// to view, plus audit metadata about denied chunks. Callers should use
/// <see cref="AllowedChunks"/> as the authoritative grounding context — denied chunks
/// must never be included in AI prompts or returned to the client.
/// </para>
/// </summary>
public sealed class AccessControlResult
{
    /// <summary>
    /// Chunks the requesting user is authorized to view, preserving the original
    /// ordering and similarity scores from the retrieval pipeline.
    /// Empty when all chunks were denied.
    /// </summary>
    public IReadOnlyList<RetrievedChunk> AllowedChunks { get; init; } =
        Array.Empty<RetrievedChunk>();

    /// <summary>
    /// Number of chunks filtered out because the user lacks access to the source document.
    /// Zero when no chunks were denied.
    /// </summary>
    public int DeniedCount { get; init; }

    /// <summary>
    /// Unique IDs of the source documents for which access was denied.
    /// Used in audit log entries (AIR-S04). Empty when no chunks were denied.
    /// </summary>
    public IReadOnlyList<Guid> DeniedDocumentIds { get; init; } =
        Array.Empty<Guid>();
}
