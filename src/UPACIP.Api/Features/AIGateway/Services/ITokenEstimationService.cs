namespace UPACIP.Api.Features.AIGateway.Services;

/// <summary>
/// Counts and truncates tokens using the GPT-4o-mini (cl100k_base) BPE encoding
/// (US_068 TASK_002, AIR-O07).
///
/// Implementations use SharpToken so token counts match the OpenAI API exactly,
/// allowing the gateway to enforce input budgets before sending bytes over the wire.
/// </summary>
public interface ITokenEstimationService
{
    /// <summary>
    /// Returns the number of BPE tokens the <paramref name="text"/> encodes to
    /// under the cl100k_base vocabulary.
    /// </summary>
    int EstimateTokenCount(string text);

    /// <summary>
    /// Returns a copy of <paramref name="text"/> truncated so that it encodes to at most
    /// <paramref name="maxTokens"/> tokens, preserving a sentence boundary where possible.
    ///
    /// If <paramref name="text"/> already fits within <paramref name="maxTokens"/>,
    /// the original string is returned unchanged (no allocation).
    /// </summary>
    string TruncateToTokenLimit(string text, int maxTokens);
}
