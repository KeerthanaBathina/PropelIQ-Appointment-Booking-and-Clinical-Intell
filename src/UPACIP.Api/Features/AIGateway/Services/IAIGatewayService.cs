using System.Security.Claims;
using UPACIP.Api.Features.AIGateway.Contracts;

namespace UPACIP.Api.Features.AIGateway.Services;

/// <summary>
/// Contract for the centralized AI Gateway orchestration service (US_067 AC-1, AC-3).
/// Consumers submit an <see cref="AIRequest"/> and receive a normalized
/// <see cref="AIResponse"/> regardless of which LLM provider fulfilled the request.
/// </summary>
public interface IAIGatewayService
{
    /// <summary>
    /// Processes an AI completion request through the full gateway pipeline:
    /// authentication check → schema/token-budget validation → provider dispatch
    /// → response normalization → structured audit log.
    ///
    /// The caller passes the current <see cref="ClaimsPrincipal"/> so the gateway
    /// can enforce Staff/Admin role enforcement without a dependency on
    /// <c>IHttpContextAccessor</c>.
    /// </summary>
    /// <param name="request">The unified AI request.</param>
    /// <param name="caller">The authenticated principal from the originating HTTP request.</param>
    /// <param name="cancellationToken">Propagated from the HTTP request lifetime.</param>
    /// <returns>A normalized <see cref="AIResponse"/>.</returns>
    Task<AIResponse> ProcessAsync(
        AIRequest request,
        ClaimsPrincipal caller,
        CancellationToken cancellationToken = default);
}
