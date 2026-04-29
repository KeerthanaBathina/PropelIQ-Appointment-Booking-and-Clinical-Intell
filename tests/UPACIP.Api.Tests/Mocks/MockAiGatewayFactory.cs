using System.Security.Claims;
using Moq;
using UPACIP.Api.Features.AIGateway.Contracts;
using UPACIP.Api.Features.AIGateway.Services;

namespace UPACIP.Api.Tests.Mocks;

/// <summary>
/// Factory for <see cref="IAIGatewayService"/> mock instances covering common test scenarios
/// (US_097, AC-1, edge case 1).
///
/// External AI providers are never called in unit tests — all AI interactions are replaced
/// with these controlled mock responses.
///
/// Variants:
/// <list type="bullet">
///   <item><see cref="CreateDefault"/> — happy path (Success=true, ConfidenceScore=0.95).</item>
///   <item><see cref="CreateFailure"/> — provider error (throws HttpRequestException).</item>
///   <item><see cref="CreateLowConfidence"/> — low-confidence response triggering review workflow.</item>
/// </list>
/// </summary>
public static class MockAiGatewayFactory
{
    /// <summary>
    /// Returns a mock that succeeds with a high-confidence response (ConfidenceScore = 0.95).
    /// Use for the happy path where AI extraction succeeds without manual review.
    /// </summary>
    public static Mock<IAIGatewayService> CreateDefault()
    {
        var mock = new Mock<IAIGatewayService>();
        mock.Setup(x => x.ProcessAsync(
                It.IsAny<AIRequest>(),
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AIResponse.Succeeded(
                requestId: Guid.NewGuid(),
                content: "Mock AI response",
                providerName: "mock",
                modelVersion: "mock-model",
                inputTokensUsed: 10,
                outputTokensUsed: 20,
                confidenceScore: 0.95f,
                latencyMs: 50));
        return mock;
    }

    /// <summary>
    /// Returns a mock that throws <see cref="HttpRequestException"/> simulating an
    /// unavailable AI provider (circuit-breaker open / network failure scenario).
    /// </summary>
    public static Mock<IAIGatewayService> CreateFailure(string errorMessage = "AI service unavailable")
    {
        var mock = new Mock<IAIGatewayService>();
        mock.Setup(x => x.ProcessAsync(
                It.IsAny<AIRequest>(),
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException(errorMessage));
        return mock;
    }

    /// <summary>
    /// Returns a mock that succeeds but with a low confidence score, triggering the
    /// hallucination-review workflow (ConfidenceScore below the 0.70 threshold).
    /// </summary>
    public static Mock<IAIGatewayService> CreateLowConfidence(float confidence = 0.45f)
    {
        var mock = new Mock<IAIGatewayService>();
        mock.Setup(x => x.ProcessAsync(
                It.IsAny<AIRequest>(),
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AIResponse.Succeeded(
                requestId: Guid.NewGuid(),
                content: "Low-confidence mock response",
                providerName: "mock",
                modelVersion: "mock-model",
                inputTokensUsed: 10,
                outputTokensUsed: 20,
                confidenceScore: confidence,
                latencyMs: 50));
        return mock;
    }
}
