using UPACIP.Api.Features.AIGateway.Contracts;
using UPACIP.Api.Features.AIGateway.Models;

namespace UPACIP.Api.Features.AIGateway.Services;

/// <summary>
/// Enforces per-request-type token budgets before requests reach an AI provider
/// (US_068 TASK_002, AIR-O01, AIR-O02, AIR-O03).
///
/// Returns a (possibly) modified <see cref="AIRequest"/> whose prompt has been
/// truncated to fit within the applicable input budget, and the effective
/// <see cref="MaxOutputTokens"/> pinned to the configured output ceiling.
/// </summary>
public interface ITokenBudgetEnforcementService
{
    /// <summary>
    /// Enforces token budget rules on <paramref name="request"/>:
    /// <list type="number">
    ///   <item>Resolve the budget ceiling for <see cref="AIRequest.RequestType"/>.</item>
    ///   <item>Count prompt tokens via <see cref="ITokenEstimationService"/>.</item>
    ///   <item>Truncate the prompt at a sentence boundary if it exceeds the input budget.</item>
    ///   <item>Pin output tokens to the budget ceiling.</item>
    /// </list>
    /// </summary>
    /// <returns>
    /// A tuple of the (potentially modified) <see cref="AIRequest"/> and an optional
    /// <see cref="TruncationMetadata"/> (non-null only when truncation occurred).
    /// </returns>
    Task<(AIRequest EnforcedRequest, TruncationMetadata? Metadata)> EnforceAsync(
        AIRequest         request,
        CancellationToken cancellationToken = default);
}
