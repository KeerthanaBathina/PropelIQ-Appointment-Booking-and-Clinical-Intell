using UPACIP.Service.VectorSearch;

namespace UPACIP.Service.Rag.Refresh.Models;

/// <summary>
/// A single code entry submitted as part of a knowledge-base refresh payload (US_078 AC-3).
///
/// Constraints validated in <see cref="KnowledgeBaseRefreshService"/>:
///   - <see cref="CodeValue"/> max 20 chars (ICD-10 / CPT codes never exceed 10; 20 allows for
///     future composite identifiers without unbounded growth).
///   - <see cref="Description"/> max 4 000 chars — aligns with the 512-token chunk window
///     (≈ 2 048 chars) with headroom for multi-chunk descriptions (AIR-O01).
///   - Null bytes stripped before passing to the chunking / embedding pipeline
///     (mirrors EmbeddingGenerationService.SanitizeText, OWASP A03).
/// </summary>
public sealed record CodeLibraryEntry
{
    /// <summary>
    /// Normalised code identifier — e.g. "E11.65", "99213".
    /// Used as the deduplication key during diff (case-insensitive comparison).
    /// </summary>
    public required string CodeValue { get; init; }

    /// <summary>
    /// Code system origin.  Accepted values: "ICD-10", "CPT".
    /// Validated at the controller layer before the service is called.
    /// </summary>
    public required string CodeSystem { get; init; }

    /// <summary>
    /// Full human-readable description that will be chunked and embedded.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// Target embedding category that determines which pgvector table is used.
    /// <see cref="EmbeddingCategory.MedicalTerminology"/> for ICD-10;
    /// <see cref="EmbeddingCategory.CodingGuideline"/> for CPT guidelines.
    /// </summary>
    public EmbeddingCategory Category { get; init; }

    /// <summary>
    /// When <c>true</c> the code is being retired in this library version.
    /// The entry will be soft-marked (<c>deprecated_at</c> stamped) rather than deleted.
    /// </summary>
    public bool IsDeprecated { get; init; }
}
