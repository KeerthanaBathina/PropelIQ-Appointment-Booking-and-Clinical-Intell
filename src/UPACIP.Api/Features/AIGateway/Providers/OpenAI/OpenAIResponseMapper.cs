using System.Text;
using OpenAI.Chat;
using UPACIP.Api.Features.AIGateway.Contracts;

namespace UPACIP.Api.Features.AIGateway.Providers.OpenAI;

/// <summary>
/// Static mapper that converts an OpenAI .NET SDK <see cref="ChatCompletion"/> into the
/// AI Gateway's unified <see cref="AIResponse"/> (US_068 TASK_001).
///
/// Concatenates all text content parts, maps token usage counters, and surfaces
/// provider metadata (name, model version). Static methods enable isolated unit testing.
/// </summary>
public static class OpenAIResponseMapper
{
    /// <summary>
    /// Converts <paramref name="completion"/> to <see cref="AIResponse.Succeeded"/>.
    ///
    /// Concatenates all text-type content parts defensively (normally exactly one
    /// part is returned; concatenation handles future streaming-chunk scenarios).
    /// </summary>
    /// <param name="completion">The raw <see cref="ChatCompletion"/> from the OpenAI SDK.</param>
    /// <param name="request">The originating <see cref="AIRequest"/> for correlation metadata.</param>
    /// <param name="providerName">Short provider identifier (e.g., <c>"openai"</c>).</param>
    /// <param name="latencyMs">End-to-end latency measured by the adapter's stopwatch.</param>
    public static AIResponse MapToResponse(
        ChatCompletion completion,
        AIRequest       request,
        string          providerName,
        long            latencyMs)
    {
        // Concatenate all text content parts (defensive — ChatCompletion.Content may be null
        // for non-text finish reasons such as ToolCalls; return empty string in that case).
        var contentBuilder = new StringBuilder();
        if (completion.Content is not null)
        {
            foreach (var part in completion.Content)
                contentBuilder.Append(part.Text);
        }

        return AIResponse.Succeeded(
            requestId:        request.RequestId,
            content:          contentBuilder.ToString(),
            providerName:     providerName,
            modelVersion:     completion.Model ?? providerName,
            inputTokensUsed:  completion.Usage?.InputTokenCount  ?? 0,
            outputTokensUsed: completion.Usage?.OutputTokenCount ?? 0,
            confidenceScore:  0f,
            latencyMs:        latencyMs);
    }
}
