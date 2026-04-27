using System.Text.RegularExpressions;
using Ganss.Xss;

namespace UPACIP.Api.Validation;

/// <summary>
/// String extension methods for XSS and command injection sanitization
/// (US_066 AC-1, FR-095, NFR-018, OWASP A03 — Injection).
///
/// SQL injection protection is handled at the ORM layer via EF Core parameterized queries;
/// this class does NOT strip single quotes or other SQL metacharacters from input values.
/// Medical names such as "O'Brien" pass through without modification (EC-1).
///
/// Thread-safety: <see cref="HtmlSanitizer"/> is thread-safe after construction.
/// All <see cref="Regex"/> instances are compiled singletons shared across requests.
/// </summary>
public static partial class SanitizationExtensions
{
    // ── HtmlSanitizer configuration ──────────────────────────────────────────
    // API inputs are data values, not rich text. All HTML markup is stripped.
    // AllowedTags/Attributes/Schemes are cleared so nothing survives sanitization.
    private static readonly HtmlSanitizer _htmlSanitizer = CreateHtmlSanitizer();

    // ── Shell metacharacter deny-list ────────────────────────────────────────
    // Covers bash and cmd.exe shell operators that could reach OS commands:
    //   |  — pipe                     &  — background / AND operator
    //   ;  — command separator        `  — command substitution
    //   $  — variable expansion       (  — subshell open
    //   )  — subshell close           >  — stdout redirect
    //   <  — stdin redirect
    // Single quotes (') are intentionally excluded — they are valid medical data (EC-1).
    [GeneratedRegex(@"[|&;`$()<>]", RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex CommandInjectionRegex();

    // ── SQL-signature deny-list (defense-in-depth, not primary SQL protection) ─
    // Covers common SQL injection comment tokens and high-risk statement keywords.
    // Primary SQL injection prevention is EF Core parameterized queries.
    // Null bytes (\x00) are included because they can bypass some validators.
    [GeneratedRegex(
        @"(--|/\*|\*/|\x00|(\b(EXEC|EXECUTE|UNION(\s+ALL)?\s+SELECT|DROP\s+TABLE|INSERT\s+INTO|DELETE\s+FROM|ALTER\s+TABLE)\b))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InjectionDenyListRegex();

    /// <summary>
    /// Strips all HTML markup and embedded JavaScript from <paramref name="value"/>
    /// using <see cref="HtmlSanitizer"/> (OWASP XSS Prevention Cheat Sheet).
    /// All tags, attributes, CSS properties, and URI schemes are disallowed —
    /// API inputs are data values, not rich text.
    ///
    /// Returns <see cref="string.Empty"/> for <c>null</c> input.
    /// Returns the original value unchanged when it contains no HTML markup.
    /// </summary>
    public static string SanitizeForXss(this string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        return _htmlSanitizer.Sanitize(value);
    }

    /// <summary>
    /// Removes shell metacharacters from <paramref name="value"/> that could be used
    /// for OS command injection: <c>|</c>, <c>&amp;</c>, <c>;</c>, <c>`</c>,
    /// <c>$</c>, <c>(</c>, <c>)</c>, <c>&gt;</c>, <c>&lt;</c>.
    ///
    /// Single quotes are NOT removed — they are valid medical data (EC-1, e.g., "O'Brien").
    /// Returns <see cref="string.Empty"/> for <c>null</c> input.
    /// </summary>
    public static string SanitizeForCommandInjection(this string? value)
    {
        if (string.IsNullOrEmpty(value)) return value ?? string.Empty;
        return CommandInjectionRegex().Replace(value, string.Empty);
    }

    /// <summary>
    /// Composite fast pre-check that returns <c>false</c> when <paramref name="value"/>
    /// matches any known SQL injection signature pattern (double-dash comment, block comment,
    /// null byte, or high-risk DML/DDL keywords).
    ///
    /// This is a deny-list guard, not a comprehensive validator. EF Core parameterized
    /// queries remain the primary SQL injection protection. Returns <c>true</c> when the
    /// input contains no known injection signature.
    /// </summary>
    public static bool IsCleanInput(this string? value)
    {
        if (string.IsNullOrEmpty(value)) return true;
        return !InjectionDenyListRegex().IsMatch(value);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static HtmlSanitizer CreateHtmlSanitizer()
    {
        var sanitizer = new HtmlSanitizer();

        // Strip all HTML — API inputs are plain data values, not rich text.
        sanitizer.AllowedTags.Clear();
        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedSchemes.Clear();

        return sanitizer;
    }
}
