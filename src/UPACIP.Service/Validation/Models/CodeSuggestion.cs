namespace UPACIP.Service.Validation.Models;

/// <summary>
/// An alternative medical code suggested when the submitted code is invalid or deprecated
/// (US_085 AC-4, DR-015).
///
/// Populated by pgvector cosine-similarity search against <c>coding_guideline_embeddings</c>
/// (384-dimension text-embedding-3-small vectors) so suggestions are semantically
/// relevant to the submitted code's meaning — not just lexically similar.
/// </summary>
public sealed record CodeSuggestion
{
    /// <summary>
    /// The ICD-10 or CPT code value (e.g. <c>"E11.65"</c>, <c>"99213"</c>).
    /// </summary>
    public string CodeValue { get; init; } = string.Empty;

    /// <summary>
    /// Code system identifier — <c>"ICD-10"</c> or <c>"CPT"</c>.
    /// </summary>
    public string CodeSystem { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable description of the suggested code
    /// (e.g. <c>"Type 2 diabetes mellitus with hyperglycemia"</c>).
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Cosine similarity score in the range [0.0, 1.0].
    /// 1.0 indicates an identical embedding vector; values below 0.5 are filtered out
    /// as insufficiently relevant (DR-015 minimum threshold).
    /// </summary>
    public double SimilarityScore { get; init; }
}
