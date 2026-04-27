namespace UPACIP.Service.Rag.Models;

/// <summary>
/// Configurable weighting and boosting options for hybrid search (US_078 AC-1, AC-2).
/// Bound from the <c>HybridSearch</c> appsettings section via the options pattern.
/// </summary>
public sealed class HybridSearchOptions
{
    public const string SectionName = "HybridSearch";

    /// <summary>
    /// Weight given to cosine similarity (vector) scores in the combined ranking.
    /// Must satisfy <c>SemanticWeight + KeywordWeight == 1.0</c>.
    /// Default: 0.7.
    /// </summary>
    public float SemanticWeight { get; init; } = 0.7f;

    /// <summary>
    /// Weight given to full-text search (FTS) scores in the combined ranking.
    /// Must satisfy <c>SemanticWeight + KeywordWeight == 1.0</c>.
    /// Default: 0.3.
    /// </summary>
    public float KeywordWeight { get; init; } = 0.3f;

    /// <summary>
    /// Multiplier applied to the final score when a chunk contains an exact whole-word match
    /// of the text query (e.g. exact ICD-10/CPT code match per US_078 edge case).
    /// Default: 2.0.
    /// </summary>
    public float ExactMatchBoostFactor { get; init; } = 2.0f;

    /// <summary>
    /// When <see langword="true"/>, exact-match detection and boosting is applied.
    /// Default: <see langword="true"/>.
    /// </summary>
    public bool EnableExactMatchBoosting { get; init; } = true;
}
