namespace UPACIP.Service.Rag.Models;

/// <summary>
/// A numbered source citation included in the prompt grounding context (US_077 AC-4, AIR-S04 audit).
/// </summary>
public sealed class SourceCitation
{
    /// <summary>1-based citation index matching the <c>[N]</c> marker in <see cref="GroundingContext.FormattedContext"/>.</summary>
    public int Index { get; init; }

    /// <summary>Source document name / section (from <see cref="RankedChunk.SourceAttribution"/>).</summary>
    public required string SourceDocument { get; init; }

    /// <summary>Truncated first 100 characters of chunk content for audit reference (AIR-S04).</summary>
    public required string ChunkPreview { get; init; }

    /// <summary>Final relevance score used when this citation was ranked (for downstream audit).</summary>
    public float RelevanceScore { get; init; }
}
