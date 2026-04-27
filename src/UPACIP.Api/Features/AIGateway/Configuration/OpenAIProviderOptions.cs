using System.ComponentModel.DataAnnotations;

namespace UPACIP.Api.Features.AIGateway.Configuration;

/// <summary>
/// Strongly-typed configuration POCO for the OpenAI GPT-4o-mini provider adapter
/// (US_067 TASK_002, AIR-O05).
///
/// Bound from the <c>"AIGateway:Providers:OpenAI"</c> configuration section.
/// Supports live model-version rollback: adapters read <c>CurrentValue</c> from
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/> on every request so
/// a configuration change (model name, version) takes effect without service restart (AIR-O05).
///
/// API keys are never logged (OWASP A02 — credential exposure).
/// </summary>
public sealed class OpenAIProviderOptions
{
    public const string SectionName = "AIGateway:Providers:OpenAI";

    /// <summary>OpenAI API key — loaded from configuration or secrets; never hardcoded.</summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>OpenAI API base URL (without trailing slash).</summary>
    [Required]
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    /// <summary>
    /// Model identifier sent in every Chat Completion request.
    /// Change this value at runtime to trigger adapter re-read without restart (AIR-O05).
    /// </summary>
    [Required]
    public string Model { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Expected model version / snapshot date for configuration-alert mismatch detection.
    /// Compared against the live model identifier returned in the response.
    /// </summary>
    public string ModelVersion { get; set; } = "2024-07-18";

    /// <summary>Per-request HTTP timeout in seconds. Defaults to 30 s.</summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; set; } = 30;
}
