using UPACIP.Service.VectorSearch;

namespace UPACIP.Service.Rag.Models;

/// <summary>
/// Request parameters for RAG context retrieval (US_077 AC-1, AC-2).
/// </summary>
public sealed class RetrievalRequest
{
    /// <summary>384-dimensional query embedding produced by the embedding service.</summary>
    public required float[] QueryEmbedding { get; init; }

    /// <summary>
    /// Original text query — used only for audit logging (never forwarded to pgvector).
    /// Must not contain PII per AIR-S04.
    /// </summary>
    public required string TextQuery { get; init; }

    /// <summary>
    /// Embedding categories (indexes) to search.
    /// Defaults to all three when null — MedicalTerminology, IntakeTemplate, CodingGuideline.
    /// </summary>
    public IReadOnlyList<EmbeddingCategory>? TargetCategories { get; init; }

    /// <summary>Number of top chunks to return after aggregating across all categories. Default: 5.</summary>
    public int TopK { get; init; } = 5;

    /// <summary>
    /// Minimum cosine similarity threshold. Results below this value are excluded (AC-2).
    /// Default: 0.75 per AIR-R02.
    /// </summary>
    public float SimilarityThreshold { get; init; } = 0.75f;

    /// <summary>
    /// When <see langword="true"/>, delegates each category search to
    /// <see cref="VectorSearch.IVectorSearchService.HybridSearchAsync"/> (vector + FTS).
    /// Default: <see langword="false"/> (pure cosine similarity).
    /// </summary>
    public bool UseHybridSearch { get; init; } = false;

    // ── Access control context (US_079 task_002, AIR-S07) ────────────────────

    /// <summary>
    /// Authenticated user's identity ID for RAG access control filtering.
    /// When <see langword="null"/> access control filtering is skipped
    /// (system-internal calls without user context).
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// The authenticated user's primary role name (e.g. "Patient", "Staff", "Admin").
    /// Used by <c>IRagAccessControlFilter</c> to apply role-appropriate document
    /// permission rules.  When <see langword="null"/> access control filtering is skipped.
    /// </summary>
    public string? UserRole { get; init; }
}
