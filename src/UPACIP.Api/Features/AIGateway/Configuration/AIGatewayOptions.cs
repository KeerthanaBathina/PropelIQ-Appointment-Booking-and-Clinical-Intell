using UPACIP.Api.Features.AIGateway.Contracts;

namespace UPACIP.Api.Features.AIGateway.Configuration;

/// <summary>
/// Strongly-typed options for the AI Gateway scaffold (US_067 AC-1, AC-3).
/// Bound from the <c>"AIGatewayOptions"</c> configuration section in
/// <c>appsettings.json</c>; registered at startup with
/// <c>ValidateDataAnnotations().ValidateOnStart()</c>.
///
/// This is separate from the existing <c>AiGatewaySettings</c> (HTTP-client keys/timeouts)
/// and focuses on gateway-level policy: token budgets, payload limits, and enabled providers.
/// </summary>
public sealed class AIGatewayOptions
{
    public const string SectionName = "AIGatewayOptions";

    // ── Provider configuration ────────────────────────────────────────────────

    /// <summary>
    /// Ordered list of provider names to try, in failover order.
    /// E.g., ["openai", "anthropic"]. First healthy provider wins.
    /// </summary>
    public IReadOnlyList<string> ProviderPriority { get; set; } = ["openai", "anthropic"];

    /// <summary>
    /// Expected model version string for each provider, keyed by <see cref="ProviderPriority"/> name.
    /// When the live <see cref="IAIProviderAdapter.ModelVersion"/> differs, a
    /// configuration-alert warning is logged without blocking the request (edge case).
    /// </summary>
    public IReadOnlyDictionary<string, string> ExpectedModelVersions { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["openai"]    = "gpt-4o-mini",
            ["anthropic"] = "claude-3-5-sonnet-20241022",
        };

    // ── Token budget overrides ────────────────────────────────────────────────
    // If set to 0 the hardcoded defaults in TokenBudget.For() are used.

    /// <summary>
    /// Maximum input tokens for <see cref="AIRequestType.DocumentParsing"/> (AIR-O01 = 4 096).
    /// </summary>
    public int DocumentParsingMaxInputTokens { get; set; } = 4_096;

    /// <summary>Maximum output tokens for DocumentParsing (AIR-O01 = 1 024).</summary>
    public int DocumentParsingMaxOutputTokens { get; set; } = 1_024;

    /// <summary>
    /// Maximum input tokens for <see cref="AIRequestType.ConversationalIntake"/> (AIR-O02 = 500).
    /// </summary>
    public int ConversationalIntakeMaxInputTokens { get; set; } = 500;

    /// <summary>Maximum output tokens for ConversationalIntake (AIR-O02 = 200).</summary>
    public int ConversationalIntakeMaxOutputTokens { get; set; } = 200;

    /// <summary>
    /// Maximum input tokens for <see cref="AIRequestType.MedicalCoding"/> (AIR-O03 = 2 048).
    /// </summary>
    public int MedicalCodingMaxInputTokens { get; set; } = 2_048;

    /// <summary>Maximum output tokens for MedicalCoding (AIR-O03 = 500).</summary>
    public int MedicalCodingMaxOutputTokens { get; set; } = 500;

    // ── Payload limits ────────────────────────────────────────────────────────

    /// <summary>Maximum allowed byte size of the serialized prompt string (default 64 KB).</summary>
    public int MaxPromptSizeBytes { get; set; } = 65_536;

    // ── Helper ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the effective <see cref="TokenBudget"/> for a given request type,
    /// applying any configuration overrides on top of the canonical defaults.
    /// </summary>
    public TokenBudget GetEffectiveBudget(AIRequestType requestType) => requestType switch
    {
        AIRequestType.DocumentParsing =>
            new TokenBudget(DocumentParsingMaxInputTokens, DocumentParsingMaxOutputTokens),

        AIRequestType.ConversationalIntake =>
            new TokenBudget(ConversationalIntakeMaxInputTokens, ConversationalIntakeMaxOutputTokens),

        AIRequestType.MedicalCoding =>
            new TokenBudget(MedicalCodingMaxInputTokens, MedicalCodingMaxOutputTokens),

        _ => TokenBudget.For(requestType),
    };
}
