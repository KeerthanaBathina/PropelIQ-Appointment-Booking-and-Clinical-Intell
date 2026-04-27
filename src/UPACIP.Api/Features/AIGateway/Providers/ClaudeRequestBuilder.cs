using System.Text.Json.Serialization;
using UPACIP.Api.Features.AIGateway.Contracts;

namespace UPACIP.Api.Features.AIGateway.Providers;

/// <summary>
/// Builds Anthropic Messages API request payloads from the unified <see cref="AIRequest"/>
/// (US_069 TASK_001, AIR-O01, AIR-O02, AIR-O03).
///
/// Anthropic-specific mapping rules:
/// <list type="bullet">
///   <item>
///     System instruction goes in the top-level <c>system</c> parameter —
///     NOT inside the <c>messages</c> array (unlike OpenAI).
///   </item>
///   <item>User prompt is the sole entry in <c>messages</c> with <c>role: "user"</c>.</item>
///   <item><c>max_tokens</c> is mandatory; defaults to the budget ceiling when not overridden.</item>
///   <item><c>temperature</c> is passed through from <see cref="AIRequest.Temperature"/>.</item>
/// </list>
///
/// Registered as Singleton — stateless, safe for concurrent access.
/// Static methods enable isolated unit testing without adapter instantiation.
/// </summary>
public sealed class ClaudeRequestBuilder
{
    /// <summary>
    /// Builds a <see cref="ClaudeMessagesPayload"/> ready for serialization and HTTP dispatch.
    /// </summary>
    /// <param name="request">The unified AI Gateway request.</param>
    /// <param name="model">Model identifier from <c>ClaudeProviderOptions.Model</c>.</param>
    /// <param name="effectiveMaxOutputTokens">Budget-clamped output ceiling.</param>
    internal ClaudeMessagesPayload Build(
        AIRequest request,
        string    model,
        int       effectiveMaxOutputTokens) =>
        new(
            Model:       model,
            MaxTokens:   effectiveMaxOutputTokens,
            Messages:    new List<ClaudeMessageBlock> { new("user", request.Prompt) },
            System:      string.IsNullOrWhiteSpace(request.SystemMessage)
                             ? null
                             : request.SystemMessage,
            Temperature: request.Temperature);
}

// ── Anthropic Messages API request DTOs ──────────────────────────────────────────
// Defined alongside the builder so they are co-located with their construction logic.
// Internal visibility — only the adapter layer deserializes/serializes these types.

/// <summary>Serializable Anthropic Messages API request body.</summary>
internal sealed record ClaudeMessagesPayload(
    [property: JsonPropertyName("model")]       string Model,
    [property: JsonPropertyName("max_tokens")]  int MaxTokens,
    [property: JsonPropertyName("messages")]    List<ClaudeMessageBlock> Messages,
    [property: JsonPropertyName("system")]      string? System,
    [property: JsonPropertyName("temperature")] float Temperature);

/// <summary>Single message entry in the <c>messages</c> array.</summary>
internal sealed record ClaudeMessageBlock(
    [property: JsonPropertyName("role")]    string Role,
    [property: JsonPropertyName("content")] string Content);
