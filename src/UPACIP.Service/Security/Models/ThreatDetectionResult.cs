namespace UPACIP.Service.Security.Models;

/// <summary>
/// Result of a single threat detection check on a string input value (US_093 task_002, AC-3).
///
/// Security note: this DTO intentionally does NOT carry the original raw input value.
/// Storing or logging the raw malicious payload would risk PII/attack data exposure.
/// Only the matched pattern name (a regex identifier, not the payload) is preserved.
/// </summary>
public sealed class ThreatDetectionResult
{
    /// <summary>True when a security threat was detected in the inspected input.</summary>
    public bool ThreatDetected { get; set; }

    /// <summary>
    /// Category of the detected threat:
    /// <list type="bullet">
    ///   <item><c>"SqlInjection"</c> — SQL comment, keyword, or tautology pattern.</item>
    ///   <item><c>"XSS"</c> — script tags, event handlers, or JavaScript protocol.</item>
    ///   <item><c>"CommandInjection"</c> — OS command separator, system command, or path traversal.</item>
    ///   <item><c>"None"</c> — no threat detected.</item>
    /// </list>
    /// </summary>
    public string ThreatType { get; set; } = "None";

    /// <summary>
    /// Name of the regex pattern that matched (e.g. "SqlKeyword", "ScriptTag").
    /// This is the pattern identifier, NOT the actual matched payload.
    /// Null when no threat was detected.
    /// </summary>
    public string? MatchedPattern { get; set; }

    /// <summary>
    /// The sanitized (safe) version of the input value.
    /// HTML-encoded, trimmed to <see cref="SecurityOptions.MaxStringInputLength"/>,
    /// and stripped of null bytes.
    /// </summary>
    public string SanitizedValue { get; set; } = string.Empty;

    /// <summary>Creates a no-threat result with the sanitized value.</summary>
    public static ThreatDetectionResult None(string sanitizedValue) => new()
    {
        ThreatDetected = false,
        ThreatType     = "None",
        SanitizedValue = sanitizedValue
    };

    /// <summary>Creates a threat-detected result for the given threat type and pattern name.</summary>
    public static ThreatDetectionResult Threat(string threatType, string patternName, string sanitizedValue) => new()
    {
        ThreatDetected = true,
        ThreatType     = threatType,
        MatchedPattern = patternName,
        SanitizedValue = sanitizedValue
    };
}
