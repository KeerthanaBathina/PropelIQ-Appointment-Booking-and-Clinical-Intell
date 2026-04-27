using UPACIP.Api.Features.AIGateway.Contracts;

namespace UPACIP.Api.Features.AIGateway.Models;

/// <summary>
/// Outcome of a token-budget validation check performed by
/// <see cref="UPACIP.Api.Features.AIGateway.Services.ITokenBudgetValidator"/>
/// (US_070 TASK_001, AIR-O01, AIR-O02, AIR-O03).
///
/// <para>When <see cref="IsWithinBudget"/> is <see langword="false"/> the caller
/// must reject the request and surface a "token budget exceeded" error to the
/// client <em>before</em> forwarding to any AI provider.</para>
/// </summary>
/// <param name="IsWithinBudget">
/// <see langword="true"/> when the actual input token count is at or below
/// <see cref="InputTokenLimit"/>; <see langword="false"/> otherwise.
/// </param>
/// <param name="RequestType">
/// The AI request category used to select the budget profile.
/// </param>
/// <param name="ActualInputTokenCount">
/// Number of input tokens counted in the request prompt by the
/// SharpToken cl100k_base BPE tokenizer.
/// </param>
/// <param name="InputTokenLimit">
/// Maximum permitted input tokens for this <see cref="RequestType"/>
/// as resolved from configuration or the hard-coded <see cref="TokenBudget"/> presets.
/// </param>
/// <param name="OutputTokenLimit">
/// Maximum permitted output tokens for this <see cref="RequestType"/>
/// as resolved from configuration or the hard-coded <see cref="TokenBudget"/> presets.
/// </param>
public sealed record TokenBudgetResult(
    bool          IsWithinBudget,
    AIRequestType RequestType,
    int           ActualInputTokenCount,
    int           InputTokenLimit,
    int           OutputTokenLimit);
