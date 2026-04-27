namespace UPACIP.Service.Rag.Models;

/// <summary>
/// Prompt-ready grounding context block with numbered source citations,
/// suitable for inclusion in downstream AI prompts (US_077 AC-4).
/// </summary>
public sealed class GroundingContext
{
    /// <summary>
    /// Fully formatted grounding block delimited by <c>[GROUNDING CONTEXT]</c> markers.
    /// Empty string when <see cref="IsGrounded"/> is <see langword="false"/>.
    /// </summary>
    public required string FormattedContext { get; init; }

    /// <summary>
    /// Individual source citations in citation-number order, used for audit logging (AIR-S04).
    /// Empty when <see cref="IsGrounded"/> is <see langword="false"/>.
    /// </summary>
    public required IReadOnlyList<SourceCitation> Citations { get; init; }

    /// <summary>
    /// <see langword="true"/> when the context contains at least one grounded chunk.
    /// <see langword="false"/> means no retrieval results met the threshold.
    /// </summary>
    public bool IsGrounded { get; init; }

    /// <summary>
    /// Either <c>"grounded"</c> or <c>"no-grounding-available"</c> for downstream
    /// AI Gateway / prompt builder consumers.
    /// </summary>
    public required string GroundingStatus { get; init; }
}
