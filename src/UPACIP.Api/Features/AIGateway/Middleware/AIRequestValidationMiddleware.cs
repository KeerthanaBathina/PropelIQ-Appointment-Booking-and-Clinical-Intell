using System.Text;
using Microsoft.Extensions.Options;
using UPACIP.Api.Features.AIGateway.Configuration;
using UPACIP.Api.Features.AIGateway.Contracts;

namespace UPACIP.Api.Features.AIGateway.Middleware;

/// <summary>
/// Validates the schema and token-budget constraints of an <see cref="AIRequest"/>
/// before it reaches any provider adapter (US_067 AC-3, AIR-O01, AIR-O02, AIR-O03).
///
/// Checks performed (in order):
///   1. Required fields are present and non-empty.
///   2. Temperature is in the valid range [0.0, 2.0].
///   3. Prompt byte size does not exceed <see cref="AIGatewayOptions.MaxPromptSizeBytes"/>.
///   4. Requested token limits do not exceed the configured budget for the request type.
///
/// Returns a <see cref="AIRequestValidationResult"/> so the caller decides how to surface
/// the failure — this keeps HTTP concerns out of the validation logic.
/// </summary>
public sealed class AIRequestValidationMiddleware
{
    private readonly AIGatewayOptions _options;

    public AIRequestValidationMiddleware(IOptions<AIGatewayOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Validates the <paramref name="request"/> and returns a validation result.
    /// </summary>
    public AIRequestValidationResult Validate(AIRequest request)
    {
        if (request is null)
            return AIRequestValidationResult.Fail("Request must not be null.");

        // 1 — Required fields
        if (string.IsNullOrWhiteSpace(request.Prompt))
            return AIRequestValidationResult.Fail("Prompt is required and must not be empty.");

        // 2 — Temperature range
        if (request.Temperature < 0f || request.Temperature > 2f)
            return AIRequestValidationResult.Fail(
                $"Temperature must be between 0.0 and 2.0 (received: {request.Temperature}).");

        // 3 — Prompt payload size
        var promptBytes = Encoding.UTF8.GetByteCount(request.Prompt);
        if (promptBytes > _options.MaxPromptSizeBytes)
            return AIRequestValidationResult.Fail(
                $"Prompt exceeds maximum allowed size of {_options.MaxPromptSizeBytes} bytes " +
                $"(received: {promptBytes} bytes).");

        // 4 — Token budget enforcement (AIR-O01, AIR-O02, AIR-O03)
        var budget = _options.GetEffectiveBudget(request.RequestType);

        if (request.MaxInputTokens > 0 && request.MaxInputTokens > budget.MaxInputTokens)
            return AIRequestValidationResult.Fail(
                $"MaxInputTokens {request.MaxInputTokens} exceeds the configured budget of " +
                $"{budget.MaxInputTokens} for request type '{request.RequestType}'.");

        if (request.MaxOutputTokens > 0 && request.MaxOutputTokens > budget.MaxOutputTokens)
            return AIRequestValidationResult.Fail(
                $"MaxOutputTokens {request.MaxOutputTokens} exceeds the configured budget of " +
                $"{budget.MaxOutputTokens} for request type '{request.RequestType}'.");

        return AIRequestValidationResult.Ok();
    }
}

/// <summary>Result of <see cref="AIRequestValidationMiddleware.Validate"/>.</summary>
public sealed record AIRequestValidationResult(bool IsValid, string? ErrorMessage)
{
    internal static AIRequestValidationResult Ok() => new(true, null);
    internal static AIRequestValidationResult Fail(string errorMessage) => new(false, errorMessage);
}
