namespace UPACIP.Api.Features.AIGateway.Configuration;

/// <summary>
/// Strongly-typed configuration POCO for per-request-type token budget limits
/// (US_068 TASK_002, AIR-O01, AIR-O02, AIR-O03).
///
/// Bound from <c>"AIGateway:TokenBudgets"</c> in <c>appsettings.json</c>.
/// Values override the hard-coded <see cref="UPACIP.Api.Features.AIGateway.Contracts.TokenBudget"/>
/// presets when present, enabling budget tuning without code changes.
///
/// Example section:
/// <code>
/// "AIGateway": {
///   "TokenBudgets": {
///     "RequestTypeBudgets": {
///       "DocumentParsing":     { "MaxInputTokens": 4000, "MaxOutputTokens": 1000 },
///       "ConversationalIntake": { "MaxInputTokens": 500,  "MaxOutputTokens": 200  },
///       "MedicalCoding":       { "MaxInputTokens": 2000, "MaxOutputTokens": 500  }
///     }
///   }
/// }
/// </code>
/// </summary>
public sealed class TokenBudgetConfiguration
{
    public const string SectionName = "AIGateway:TokenBudgets";

    /// <summary>
    /// Per-request-type budget limits keyed by <see cref="Contracts.AIRequestType"/> name
    /// (e.g., <c>"DocumentParsing"</c>, <c>"ConversationalIntake"</c>, <c>"MedicalCoding"</c>).
    /// </summary>
    public Dictionary<string, TokenBudgetLimit> RequestTypeBudgets { get; set; } = new();
}

/// <summary>Input/output token ceilings for a single request type.</summary>
public sealed class TokenBudgetLimit
{
    /// <summary>Maximum number of input (prompt) tokens allowed.</summary>
    public int MaxInputTokens { get; set; }

    /// <summary>Maximum number of output (completion) tokens the provider may generate.</summary>
    public int MaxOutputTokens { get; set; }
}
