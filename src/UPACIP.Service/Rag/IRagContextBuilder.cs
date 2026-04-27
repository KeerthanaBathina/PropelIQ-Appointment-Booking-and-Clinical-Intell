using UPACIP.Service.Rag.Models;

namespace UPACIP.Service.Rag;

/// <summary>
/// Formats re-ranked chunks into a prompt-ready grounding context block with numbered
/// source citations for inclusion in downstream AI prompts (US_077 AC-4).
///
/// Produces <c>[GROUNDING CONTEXT]…[/GROUNDING CONTEXT]</c> delimited blocks.
/// When no chunks are available (<see cref="RerankResult.Chunks"/> is empty),
/// returns an empty context with <c>IsGrounded = false</c> and
/// <c>GroundingStatus = "no-grounding-available"</c>.
/// </summary>
public interface IRagContextBuilder
{
    /// <summary>
    /// Formats the re-ranked <paramref name="rerankResult"/> into a structured
    /// <see cref="GroundingContext"/> suitable for prompt injection.
    /// </summary>
    Task<GroundingContext> BuildContextAsync(
        RerankResult rerankResult,
        CancellationToken cancellationToken = default);
}
