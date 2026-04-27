using System.ComponentModel.DataAnnotations;

namespace UPACIP.Api.Features.AIGateway.Configuration;

/// <summary>
/// Strongly-typed configuration POCO for the Anthropic Claude 3.5 Sonnet fallback
/// provider adapter (US_067 TASK_002, AIR-O05).
///
/// Bound from the <c>"AIGateway:Providers:Claude"</c> configuration section.
/// Supports live model-version rollback: adapters read <c>CurrentValue</c> from
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/> on every request so
/// a configuration change (model name, version) takes effect without service restart (AIR-O05).
///
/// API keys are never logged (OWASP A02 — credential exposure).
/// </summary>
public sealed class ClaudeProviderOptions
{
    public const string SectionName = "AIGateway:Providers:Claude";

    /// <summary>Anthropic API key — loaded from configuration or secrets; never hardcoded.</summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Anthropic API base URL (without trailing slash).</summary>
    [Required]
    public string BaseUrl { get; set; } = "https://api.anthropic.com/v1";

    /// <summary>
    /// Model identifier sent in every Messages API request.
    /// Change this value at runtime to trigger adapter re-read without restart (AIR-O05).
    /// </summary>
    [Required]
    public string Model { get; set; } = "claude-3-5-sonnet-20241022";

    /// <summary>
    /// Anthropic API version header value sent with every request.
    /// Required by the Anthropic Messages API.
    /// </summary>
    public string AnthropicVersion { get; set; } = "2023-06-01";

    /// <summary>Per-request HTTP timeout in seconds. Defaults to 30 s.</summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Priority order of this provider in the fallback chain (lower = higher priority).
    /// Primary provider (OpenAI) is priority 1; Claude fallback defaults to 2.
    /// </summary>
    public int FallbackPriority { get; set; } = 2;

    /// <summary>
    /// Maximum number of concurrent fallback requests permitted to this provider.
    /// Prevents overloading Anthropic when primary is down. Defaults to 3.
    /// </summary>
    [Range(1, 20)]
    public int MaxConcurrentFallbackRequests { get; set; } = 3;
}
