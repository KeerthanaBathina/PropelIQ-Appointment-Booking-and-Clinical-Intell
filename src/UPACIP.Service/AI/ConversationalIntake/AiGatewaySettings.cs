namespace UPACIP.Service.AI.ConversationalIntake;

/// <summary>
/// Strongly-typed binding for the AI Gateway settings section in appsettings.json.
/// Keys are never logged (OWASP A02 — Cryptographic Failures / credential exposure).
/// </summary>
public sealed class AiGatewaySettings
{
    public const string SectionName = "AiGateway";

    /// <summary>OpenAI API key — loaded from configuration, never hardcoded.</summary>
    public string OpenAiApiKey { get; init; } = string.Empty;

    /// <summary>OpenAI model identifier (primary provider).</summary>
    public string OpenAiModel { get; init; } = "gpt-4o-mini";

    /// <summary>OpenAI API base URL.</summary>
    public string OpenAiBaseUrl { get; init; } = "https://api.openai.com";

    /// <summary>Anthropic API key — loaded from configuration, never hardcoded.</summary>
    public string AnthropicApiKey { get; init; } = string.Empty;

    /// <summary>Anthropic model identifier (fallback provider).</summary>
    public string AnthropicModel { get; init; } = "claude-3-5-sonnet-20241022";

    /// <summary>Anthropic API base URL.</summary>
    public string AnthropicBaseUrl { get; init; } = "https://api.anthropic.com";

    /// <summary>Per-request timeout in seconds (enforced by HttpClient).</summary>
    public int TimeoutSeconds { get; init; } = 10;
}
