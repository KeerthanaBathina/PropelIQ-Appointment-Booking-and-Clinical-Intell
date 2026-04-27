using UPACIP.Api.Features.AIGateway.Contracts;

namespace UPACIP.Api.Features.AIGateway.Services;

/// <summary>
/// Extracts token usage from an <see cref="AIResponse"/>, calculates estimated cost
/// using the provider rate card from <c>AiCostBudgetConfig</c>, and persists an
/// <c>AiRequestLog</c> row for every AI provider call (US_071 TASK_004, AC-1).
///
/// <para>
/// Both OpenAI and Anthropic provider adapters already populate
/// <see cref="AIResponse.InputTokensUsed"/> and <see cref="AIResponse.OutputTokensUsed"/>
/// from the respective provider response objects (<c>usage.prompt_tokens / completion_tokens</c>
/// for OpenAI; <c>usage.input_tokens / output_tokens</c> for Anthropic).
/// This service consumes those normalized counts — no raw HTTP response parsing required here.
/// </para>
///
/// <para>
/// Cost source is always <c>AiCostSource.Approximate</c> because neither provider API returns
/// a dollar-denominated cost in the response payload; cost is always derived from the rate card
/// (<c>token_count × cost_per_1k / 1000</c>) stored in <c>AiCostBudgetConfig</c>.
/// </para>
/// </summary>
public interface IAiRequestCostLogger
{
    /// <summary>
    /// Extracts token usage from <paramref name="response"/>, applies the provider rate card,
    /// and appends an <c>AiRequestLog</c> entry to the database.
    /// </summary>
    /// <param name="request">The originating AI request (provides request type and correlation ID).</param>
    /// <param name="response">The provider response (provides token counts and provider name).</param>
    /// <param name="ct">Propagated cancellation token.</param>
    Task LogAsync(AIRequest request, AIResponse response, CancellationToken ct = default);
}
