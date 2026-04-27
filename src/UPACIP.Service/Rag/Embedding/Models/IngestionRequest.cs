using UPACIP.Service.VectorSearch;

namespace UPACIP.Service.Rag.Embedding.Models;

/// <summary>
/// End-to-end document ingestion request: chunk → embed → store (US_076 AC-2, Implementation Plan §6).
/// </summary>
public sealed class IngestionRequest
{
    /// <summary>Full document text to be chunked and embedded.</summary>
    public required string DocumentText { get; init; }

    /// <summary>Stable identifier for the source document — used to derive deterministic chunk IDs.</summary>
    public Guid SourceDocumentId { get; init; }

    /// <summary>Human-readable source name used in log messages (never contains PII per AIR-S04).</summary>
    public required string SourceName { get; init; }

    /// <summary>Embedding category that determines the target pgvector table (AIR-R04).</summary>
    public EmbeddingCategory Category { get; init; }
}
