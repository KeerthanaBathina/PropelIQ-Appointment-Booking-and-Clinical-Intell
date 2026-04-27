using System.Text;
using UPACIP.Service.Rag.Models;

namespace UPACIP.Service.Rag;

/// <summary>
/// Stateless implementation of <see cref="IRagContextBuilder"/> that formats re-ranked
/// chunks into numbered citation blocks for AI prompt grounding (US_077 AC-4, AIR-S04).
///
/// Output format:
/// <code>
/// [GROUNDING CONTEXT]
/// [1] (Source: {SourceAttribution}, Relevance: 0.93)
/// {ChunkContent}
///
/// [2] (Source: {SourceAttribution}, Relevance: 0.87)
/// {ChunkContent}
/// [/GROUNDING CONTEXT]
/// </code>
///
/// When no chunks are available, returns an empty formatted context with
/// <c>GroundingStatus = "no-grounding-available"</c> so downstream consumers
/// can attach the flag to AI responses for staff awareness.
/// </summary>
public sealed class RagContextBuilder : IRagContextBuilder
{
    private const string GroundedStatus    = "grounded";
    private const string NoGroundingStatus = "no-grounding-available";
    private const int    ChunkPreviewChars = 100;

    /// <inheritdoc/>
    public Task<GroundingContext> BuildContextAsync(
        RerankResult rerankResult,
        CancellationToken cancellationToken = default)
    {
        if (rerankResult.Chunks.Count == 0)
        {
            return Task.FromResult(new GroundingContext
            {
                FormattedContext = string.Empty,
                Citations        = [],
                IsGrounded       = false,
                GroundingStatus  = NoGroundingStatus,
            });
        }

        var sb       = new StringBuilder();
        var citations = new List<SourceCitation>(rerankResult.Chunks.Count);

        sb.AppendLine("[GROUNDING CONTEXT]");

        foreach (var chunk in rerankResult.Chunks)
        {
            var citationIndex = chunk.FinalRank;

            sb.AppendLine(
                $"[{citationIndex}] (Source: {chunk.SourceAttribution}, " +
                $"Relevance: {chunk.RelevanceScore:F2})");
            sb.AppendLine(chunk.Content);
            sb.AppendLine();

            var preview = chunk.Content.Length > ChunkPreviewChars
                ? string.Concat(chunk.Content.AsSpan(0, ChunkPreviewChars), "…")
                : chunk.Content;

            citations.Add(new SourceCitation
            {
                Index          = citationIndex,
                SourceDocument = chunk.SourceAttribution,
                ChunkPreview   = preview,
                RelevanceScore = chunk.RelevanceScore,
            });
        }

        sb.Append("[/GROUNDING CONTEXT]");

        return Task.FromResult(new GroundingContext
        {
            FormattedContext = sb.ToString(),
            Citations        = citations,
            IsGrounded       = true,
            GroundingStatus  = GroundedStatus,
        });
    }
}
