using UPACIP.Api.Features.AIGateway.Contracts;

namespace UPACIP.Api.Features.AIGateway.Models;

/// <summary>
/// Records the details of an input-truncation event performed by
/// <c>TokenBudgetEnforcementService</c> (US_068 TASK_002, AIR-O01–AIR-O03).
///
/// Attached to <see cref="AIResponse.TruncationInfo"/> whenever the caller's
/// prompt exceeded the per-request-type token budget and was trimmed before
/// being forwarded to the provider.
/// </summary>
public sealed record TruncationMetadata(
    /// <summary>Token count of the original, untruncated prompt.</summary>
    int OriginalTokenCount,

    /// <summary>Token count of the prompt after truncation.</summary>
    int TruncatedTokenCount,

    /// <summary>The budget ceiling that triggered truncation.</summary>
    int MaxAllowedTokens,

    /// <summary>The request type whose budget limit was applied.</summary>
    AIRequestType RequestType);
