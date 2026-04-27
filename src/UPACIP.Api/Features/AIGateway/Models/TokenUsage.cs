namespace UPACIP.Api.Features.AIGateway.Models;

/// <summary>
/// Unified token usage record normalized across AI providers (US_071 TASK_004).
///
/// OpenAI reports usage as <c>prompt_tokens</c> / <c>completion_tokens</c>;
/// Anthropic reports it as <c>input_tokens</c> / <c>output_tokens</c>.
/// Both are mapped into this record so cost calculation is provider-agnostic.
/// </summary>
/// <param name="InputTokens">Number of input (prompt) tokens consumed.</param>
/// <param name="OutputTokens">Number of output (completion) tokens generated.</param>
public sealed record TokenUsage(int InputTokens, int OutputTokens)
{
    /// <summary>Sum of input and output tokens.</summary>
    public int TotalTokens => InputTokens + OutputTokens;

    /// <summary>
    /// Returns <see langword="true"/> when at least one token count is populated,
    /// indicating that a real provider call was made and token data is available.
    /// </summary>
    public bool HasData => InputTokens > 0 || OutputTokens > 0;

    /// <summary>Empty usage record — used when token data is unavailable.</summary>
    public static readonly TokenUsage Empty = new(0, 0);
}
