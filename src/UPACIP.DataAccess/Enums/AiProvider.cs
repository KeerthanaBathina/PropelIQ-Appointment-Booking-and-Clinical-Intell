namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Identifies the AI model provider used for a given request or cost record.
/// Stored as a string in the database via EF Core <c>HasConversion&lt;string&gt;()</c>
/// so that schema remains human-readable without requiring a lookup join (US_071 TASK_001).
/// </summary>
public enum AiProvider
{
    /// <summary>OpenAI GPT-4o-mini — primary AI provider (AIR-O01, AIR-O02, AIR-O03).</summary>
    OpenAI = 1,

    /// <summary>Anthropic Claude 3.5 Sonnet — fallback AI provider (US_069).</summary>
    Anthropic = 2,
}
