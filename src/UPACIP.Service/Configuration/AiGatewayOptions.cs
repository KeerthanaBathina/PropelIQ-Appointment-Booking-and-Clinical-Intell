namespace UPACIP.Service.Configuration;

/// <summary>
/// Strongly-typed gateway-level options for the AI provider integration (US_101, AC-4).
/// Bound from the <c>AiGateway</c> section in <c>appsettings.json</c>.
///
/// This class captures gateway-policy settings (base URLs, model names, token budgets).
/// It is intentionally separate from <c>UPACIP.Api.Features.AIGateway.Configuration.AIGatewayOptions</c>,
/// which covers provider priority, per-operation token overrides, and payload limits —
/// concerns owned by the AI Gateway feature module.
/// </summary>
public sealed class AiGatewayOptions
{
    /// <summary>Configuration section name used for <c>IOptions&lt;T&gt;</c> binding.</summary>
    public const string SectionName = "AiGatewayCfg";

    /// <summary>Base URL of the primary AI provider (e.g. <c>https://api.openai.com</c>).</summary>
    public string PrimaryProviderBaseUrl { get; init; } = string.Empty;

    /// <summary>Base URL of the fallback AI provider (e.g. <c>https://api.anthropic.com</c>).</summary>
    public string FallbackProviderBaseUrl { get; init; } = string.Empty;

    /// <summary>Model identifier for the primary provider.  Default: <c>gpt-4o-mini</c>.</summary>
    public string PrimaryModel { get; init; } = "gpt-4o-mini";

    /// <summary>Model identifier for the fallback provider.  Default: <c>claude-3-5-sonnet-20241022</c>.</summary>
    public string FallbackModel { get; init; } = "claude-3-5-sonnet-20241022";

    /// <summary>Global upper bound on tokens per request (input + output).  Default: 4096.</summary>
    public int MaxTokensPerRequest { get; init; } = 4096;

    /// <summary>HTTP request timeout for AI API calls in seconds.  Default: 30.</summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Minimum model confidence score (0.0–1.0) below which results are flagged for
    /// human review.  Default: 0.8.
    /// </summary>
    public double ConfidenceThreshold { get; init; } = 0.8;
}
