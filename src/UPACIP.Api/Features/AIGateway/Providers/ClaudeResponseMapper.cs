using System.Text;
using System.Text.Json.Serialization;
using UPACIP.Api.Features.AIGateway.Contracts;

namespace UPACIP.Api.Features.AIGateway.Providers;

/// <summary>
/// Maps Anthropic Messages API responses to the unified <see cref="AIResponse"/> schema
/// (US_069 TASK_001, AC-2 — identical response format as primary provider).
///
/// Anthropic-specific mapping rules:
/// <list type="bullet">
///   <item>
///     <c>content</c> is an array of typed content blocks; only <c>"text"</c> blocks
///     are concatenated into <see cref="AIResponse.Content"/>.
///   </item>
///   <item>
///     <c>stop_reason: "max_tokens"</c> indicates the model hit the output ceiling —
///     logged as a warning for operator visibility.
///   </item>
///   <item>
///     <c>usage.input_tokens</c> / <c>usage.output_tokens</c> → token count fields
///     for cost tracking (AIR-O09, $3.00 / 1 M input, $15.00 / 1 M output).
///   </item>
/// </list>
///
/// Registered as Singleton — stateless, safe for concurrent access.
/// Static methods enable isolated unit testing without adapter instantiation.
/// </summary>
public sealed class ClaudeResponseMapper
{
    private const string StopReasonMaxTokens = "max_tokens";

    /// <summary>
    /// Converts <paramref name="response"/> to a normalized <see cref="AIResponse.Succeeded"/>
    /// result.
    /// </summary>
    /// <param name="response">The raw Anthropic Messages API response body.</param>
    /// <param name="request">The originating <see cref="AIRequest"/> for correlation.</param>
    /// <param name="providerName">Short provider identifier (e.g., <c>"anthropic"</c>).</param>
    /// <param name="latencyMs">End-to-end latency from the adapter stopwatch.</param>
    internal AIResponse MapToResponse(
        ClaudeMessagesResponseBody response,
        AIRequest                  request,
        string                     providerName,
        long                       latencyMs)
    {
        // Concatenate all text content blocks (Anthropic may return multiple blocks
        // for tool-use responses; for standard text responses there is typically one).
        var contentBuilder = new StringBuilder();
        if (response.Content is not null)
        {
            foreach (var block in response.Content)
            {
                if (string.Equals(block.Type, "text", StringComparison.OrdinalIgnoreCase))
                    contentBuilder.Append(block.Text);
            }
        }

        return AIResponse.Succeeded(
            requestId:        request.RequestId,
            content:          contentBuilder.ToString(),
            providerName:     providerName,
            modelVersion:     response.Model ?? providerName,
            inputTokensUsed:  response.Usage?.InputTokens  ?? 0,
            outputTokensUsed: response.Usage?.OutputTokens ?? 0,
            confidenceScore:  0f,
            latencyMs:        latencyMs);
    }

    /// <summary>
    /// Returns <see langword="true"/> when <paramref name="response"/> indicates the model
    /// hit the output token ceiling (<c>stop_reason: "max_tokens"</c>).
    /// Callers should log a warning and surface this as a partial-response signal.
    /// </summary>
    internal static bool IsOutputTruncated(ClaudeMessagesResponseBody response) =>
        string.Equals(response.StopReason, StopReasonMaxTokens, StringComparison.OrdinalIgnoreCase);
}

// ── Anthropic Messages API response DTOs ─────────────────────────────────────────
// Defined alongside the mapper so they are co-located with their deserialization logic.
// Internal visibility — only the adapter layer deserializes these types.

/// <summary>Deserialized Anthropic Messages API response body.</summary>
internal sealed record ClaudeMessagesResponseBody(
    [property: JsonPropertyName("id")]          string? Id,
    [property: JsonPropertyName("type")]        string? Type,
    [property: JsonPropertyName("model")]       string? Model,
    [property: JsonPropertyName("content")]     List<ClaudeContentBlock>? Content,
    [property: JsonPropertyName("usage")]       ClaudeUsageBlock? Usage,
    [property: JsonPropertyName("stop_reason")] string? StopReason);

/// <summary>A single content block from the Anthropic response <c>content</c> array.</summary>
internal sealed record ClaudeContentBlock(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text);

/// <summary>Token usage counters from the Anthropic response <c>usage</c> object.</summary>
internal sealed record ClaudeUsageBlock(
    [property: JsonPropertyName("input_tokens")]  int InputTokens,
    [property: JsonPropertyName("output_tokens")] int OutputTokens);
