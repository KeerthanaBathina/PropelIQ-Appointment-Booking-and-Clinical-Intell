using UPACIP.Api.Features.AIGateway.Contracts;

namespace UPACIP.Api.Features.AIGateway.Middleware;

/// <summary>
/// Maps provider-specific response payloads to the normalized <see cref="AIResponse"/>
/// contract (US_067 AC-3).
///
/// Provider adapters already return an <see cref="AIResponse"/>, but raw adapter
/// responses may include partial success states, empty content, or inconsistent
/// confidence scores. This middleware component applies cross-cutting normalization:
///
///   1. Ensures <see cref="AIResponse.Content"/> is never null — set to empty string on failure.
///   2. Clamps <see cref="AIResponse.ConfidenceScore"/> to [0.0, 1.0].
///   3. Coerces negative token counts to zero (provider SDK inconsistency guard).
///   4. Applies a final <see cref="AIResponse.Success"/> check: if Content is empty
///      after a "successful" response the result is demoted to a failed response.
///
/// This component is intentionally stateless and pure — it does not make I/O calls.
/// </summary>
public sealed class AIResponseNormalizationMiddleware
{
    /// <summary>
    /// Normalizes the <paramref name="rawResponse"/> returned by a provider adapter
    /// and returns a contract-safe <see cref="AIResponse"/>.
    /// </summary>
    public AIResponse Normalize(AIResponse rawResponse)
    {
        if (rawResponse is null)
            return AIResponse.Failed(Guid.Empty, "Provider returned a null response.");

        // 1 — Guard against null content
        var content = rawResponse.Content ?? string.Empty;

        // 2 — Clamp confidence score
        var confidence = Math.Clamp(rawResponse.ConfidenceScore, 0f, 1f);

        // 3 — Coerce negative token counts
        var inputTokens  = Math.Max(rawResponse.InputTokensUsed,  0);
        var outputTokens = Math.Max(rawResponse.OutputTokensUsed, 0);

        // 4 — Demote empty-content "success" to failure
        var success      = rawResponse.Success && !string.IsNullOrWhiteSpace(content);
        var errorMessage = success
            ? null
            : rawResponse.ErrorMessage
              ?? (rawResponse.Success
                  ? "Provider returned an empty response. Retry or escalate to fallback."
                  : null);

        return new AIResponse
        {
            RequestId        = rawResponse.RequestId,
            Content          = content,
            ProviderName     = rawResponse.ProviderName ?? string.Empty,
            ModelVersion     = rawResponse.ModelVersion ?? string.Empty,
            InputTokensUsed  = inputTokens,
            OutputTokensUsed = outputTokens,
            ConfidenceScore  = confidence,
            LatencyMs        = Math.Max(rawResponse.LatencyMs, 0),
            Success          = success,
            ErrorMessage     = errorMessage,
        };
    }
}
