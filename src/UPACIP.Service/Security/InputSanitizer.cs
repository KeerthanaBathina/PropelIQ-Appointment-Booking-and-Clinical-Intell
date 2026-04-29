using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.Service.Security.Models;

namespace UPACIP.Service.Security;

/// <summary>
/// Centralized input sanitization and threat detection service (US_093 task_002, AC-3,
/// NFR-018, OWASP A03 — Injection).
///
/// Provides three detection methods (SQL injection, XSS, command injection) and a
/// sanitize method. Detection methods return <see cref="ThreatDetectionResult"/> with
/// pass/fail status and the matched pattern name — never the raw payload.
///
/// Primary SQL injection defense is EF Core parameterized queries. This service provides
/// a secondary application-level detection layer that blocks obviously malicious inputs
/// before they reach business logic.
/// </summary>
public interface IInputSanitizer
{
    /// <summary>
    /// Detects SQL injection patterns in the input string.
    /// Primary SQL injection defense is EF Core parameterized queries (NFR-018).
    /// This is a secondary detection layer.
    /// </summary>
    ThreatDetectionResult DetectSqlInjection(string input);

    /// <summary>Detects XSS attack patterns including script tags, event handlers, and JavaScript protocols.</summary>
    ThreatDetectionResult DetectXss(string input);

    /// <summary>Detects OS command injection patterns including shell operators, system commands, and path traversal.</summary>
    ThreatDetectionResult DetectCommandInjection(string input);

    /// <summary>
    /// Sanitizes input by HTML-encoding, removing null bytes, normalizing Unicode,
    /// and trimming to max length. Returns a safe value suitable for storage or display.
    /// </summary>
    string Sanitize(string input);
}

/// <summary>
/// Scoped implementation of <see cref="IInputSanitizer"/>.
/// All regex instances are compiled singletons — thread-safe across requests.
/// </summary>
public sealed partial class InputSanitizer : IInputSanitizer
{
    private readonly IOptionsMonitor<SecurityOptions> _options;
    private readonly ILogger<InputSanitizer>          _logger;

    public InputSanitizer(
        IOptionsMonitor<SecurityOptions> options,
        ILogger<InputSanitizer>          logger)
    {
        _options = options;
        _logger  = logger;
    }

    // ── SQL injection patterns ───────────────────────────────────────────────

    // DML/DDL keyword pairs that indicate injection (secondary detection layer)
    [GeneratedRegex(
        @"(?i)\b(SELECT|INSERT|UPDATE|DELETE|DROP|ALTER|EXEC|EXECUTE|UNION|CREATE|TRUNCATE)\b.*\b(FROM|INTO|TABLE|SET|WHERE|DATABASE|SCHEMA)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SqlKeywordPairRegex();

    // SQL comment sequences
    [GeneratedRegex(@"--|/\*|\*/", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex SqlCommentRegex();

    // String termination + SQL keyword (classic injection vector)
    [GeneratedRegex(
        @"';\s*(SELECT|DROP|INSERT|UPDATE|DELETE)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SqlTerminationRegex();

    // Tautology attacks
    [GeneratedRegex(
        @"'\s*OR\s*'?\s*1\s*'?\s*=\s*'?\s*1|'\s*OR\s+1\s*=\s*1",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SqlTautologyRegex();

    // ── XSS patterns ────────────────────────────────────────────────────────

    // Script open/close tags
    [GeneratedRegex(@"<script[^>]*>|</script>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptTagRegex();

    // Inline event handler attributes
    [GeneratedRegex(
        @"on(load|error|click|mouseover|focus|blur|submit|change|input)\s*=",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EventHandlerRegex();

    // JavaScript/VBScript protocol and data: URI with HTML content
    [GeneratedRegex(
        @"javascript:|vbscript:|data:text/html",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptProtocolRegex();

    // Encoded script injection (HTML entity / percent / Unicode encoding)
    [GeneratedRegex(
        @"&#x3C;script|%3Cscript|\u003cscript",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EncodedScriptRegex();

    // ── Command injection patterns ───────────────────────────────────────────

    // OS command separators (excluding single quote which is valid medical data e.g. O'Brien)
    [GeneratedRegex(@";\s+| && | \|\| | \| |`", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex CommandSeparatorRegex();

    // Common OS commands
    [GeneratedRegex(
        @"(?i)\b(cmd|powershell|bash|sh|wget|curl|net\s+user|whoami|cat\s+/etc|rm\s+-rf)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OsCommandRegex();

    // Path traversal sequences
    [GeneratedRegex(@"\.\.[\\/]|%2e%2e%2f", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PathTraversalRegex();

    // ── Public methods ───────────────────────────────────────────────────────

    /// <inheritdoc/>
    public ThreatDetectionResult DetectSqlInjection(string input)
    {
        if (string.IsNullOrEmpty(input))
            return ThreatDetectionResult.None(input);

        var sanitized = Sanitize(input);

        if (SqlKeywordPairRegex().IsMatch(input))
            return ThreatDetectionResult.Threat("SqlInjection", "SqlKeyword", sanitized);

        if (SqlCommentRegex().IsMatch(input))
            return ThreatDetectionResult.Threat("SqlInjection", "SqlComment", sanitized);

        if (SqlTerminationRegex().IsMatch(input))
            return ThreatDetectionResult.Threat("SqlInjection", "SqlTermination", sanitized);

        if (SqlTautologyRegex().IsMatch(input))
            return ThreatDetectionResult.Threat("SqlInjection", "SqlTautology", sanitized);

        return ThreatDetectionResult.None(sanitized);
    }

    /// <inheritdoc/>
    public ThreatDetectionResult DetectXss(string input)
    {
        if (string.IsNullOrEmpty(input))
            return ThreatDetectionResult.None(input);

        var sanitized = Sanitize(input);

        if (ScriptTagRegex().IsMatch(input))
            return ThreatDetectionResult.Threat("XSS", "ScriptTag", sanitized);

        if (EventHandlerRegex().IsMatch(input))
            return ThreatDetectionResult.Threat("XSS", "EventHandler", sanitized);

        if (ScriptProtocolRegex().IsMatch(input))
            return ThreatDetectionResult.Threat("XSS", "ScriptProtocol", sanitized);

        if (EncodedScriptRegex().IsMatch(input))
            return ThreatDetectionResult.Threat("XSS", "EncodedScript", sanitized);

        return ThreatDetectionResult.None(sanitized);
    }

    /// <inheritdoc/>
    public ThreatDetectionResult DetectCommandInjection(string input)
    {
        if (string.IsNullOrEmpty(input))
            return ThreatDetectionResult.None(input);

        var sanitized = Sanitize(input);

        if (CommandSeparatorRegex().IsMatch(input))
            return ThreatDetectionResult.Threat("CommandInjection", "CommandSeparator", sanitized);

        if (OsCommandRegex().IsMatch(input))
            return ThreatDetectionResult.Threat("CommandInjection", "OsCommand", sanitized);

        if (PathTraversalRegex().IsMatch(input))
            return ThreatDetectionResult.Threat("CommandInjection", "PathTraversal", sanitized);

        return ThreatDetectionResult.None(sanitized);
    }

    /// <inheritdoc/>
    public string Sanitize(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var maxLen = _options.CurrentValue.MaxStringInputLength;

        // Remove null bytes (can bypass some validators)
        var cleaned = input.Replace("\0", string.Empty, StringComparison.Ordinal);

        // Normalize Unicode to NFC to prevent homoglyph attacks
        cleaned = cleaned.Normalize(NormalizationForm.FormC);

        // Trim to max length
        if (cleaned.Length > maxLen)
            cleaned = cleaned[..maxLen];

        // HTML-encode to prevent XSS in any response context (OWASP XSS Prevention)
        return WebUtility.HtmlEncode(cleaned);
    }
}
