using System.Text.RegularExpressions;

namespace UPACIP.Service.AiSafety;

/// <summary>
/// Regex-based PII detection and redaction service implementing Platt-ordered category
/// scanning with medical terminology allowlist filtering (US_074 task_001, AC-3, AC-4, AIR-S01).
///
/// <para>Registered as Singleton — all regex patterns are pre-compiled at class initialisation
/// and the class holds no per-request mutable state.</para>
///
/// <para>Regex timeout of 100 ms per pattern prevents catastrophic backtracking on
/// adversarial input (OWASP A03 injection defense).</para>
/// </summary>
public sealed class PiiRedactionService : IPiiRedactionService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Regex timeout — prevents catastrophic backtracking on adversarial input
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    // ─────────────────────────────────────────────────────────────────────────
    // PII category regex patterns (compiled at class-init for performance)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Social Security Number — most specific; matched first.
    /// Supports both hyphenated (123-45-6789) and bare (123456789) formats.
    /// </summary>
    private static readonly Regex SsnPattern = new(
        @"\b\d{3}-?\d{2}-?\d{4}\b",
        RegexOptions.Compiled, RegexTimeout);

    /// <summary>
    /// Email address — RFC 5322 simplified pattern.
    /// </summary>
    private static readonly Regex EmailPattern = new(
        @"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b",
        RegexOptions.Compiled, RegexTimeout);

    /// <summary>
    /// US phone numbers — various separator styles including international prefix.
    /// </summary>
    private static readonly Regex PhonePattern = new(
        @"\b(\+?1[-.\s]?)?\(?\d{3}\)?[-.\s]?\d{3}[-.\s]?\d{4}\b",
        RegexOptions.Compiled, RegexTimeout);

    /// <summary>
    /// Date of birth — MM/DD/YYYY, MM-DD-YYYY, MM/DD/YY and ISO 8601 (YYYY-MM-DD).
    /// </summary>
    private static readonly Regex DobPattern = new(
        @"\b\d{1,2}[/\-]\d{1,2}[/\-]\d{2,4}\b|\b\d{4}-\d{2}-\d{2}\b",
        RegexOptions.Compiled, RegexTimeout);

    /// <summary>
    /// US street address — number + street name + common suffixes + optional zip code.
    /// </summary>
    private static readonly Regex AddressPattern = new(
        @"\b\d{1,5}\s+\w[\w\s]{1,40}(?:St(?:reet)?|Ave(?:nue)?|Blvd|Rd|Road|Dr(?:ive)?|Ln|Lane|Ct|Court|Pl(?:ace)?|Way)\b(?:\s*,?\s*\w[\w\s]{1,30})?\b(?:\s+\d{5}(?:-\d{4})?)?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase, RegexTimeout);

    // ─────────────────────────────────────────────────────────────────────────
    // IPiiRedactionService implementation
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public (string redactedText, PiiRedactionContext context) RedactPii(
        string  inputText,
        Guid    patientId,
        string? patientName  = null,
        string? dateOfBirth  = null,
        string? phoneNumber  = null)
    {
        if (string.IsNullOrEmpty(inputText))
            return (inputText, new PiiRedactionContext(patientId));

        var ctx  = new PiiRedactionContext(patientId);
        string text = inputText;

        // ── 1. SSN (most specific) ────────────────────────────────────────────
        text = RedactPattern(text, SsnPattern, "SSN", ctx);

        // ── 2. Email ──────────────────────────────────────────────────────────
        text = RedactPattern(text, EmailPattern, "EMAIL", ctx);

        // ── 3. Phone — regex first, then exact patient phone if provided ──────
        text = RedactPattern(text, PhonePattern, "PHONE", ctx);
        if (!string.IsNullOrWhiteSpace(phoneNumber))
            text = RedactExact(text, phoneNumber, "PHONE", ctx);

        // ── 4. DOB — regex first, then exact patient DOB if provided ─────────
        text = RedactPattern(text, DobPattern, "DOB", ctx);
        if (!string.IsNullOrWhiteSpace(dateOfBirth))
            text = RedactExact(text, dateOfBirth, "DOB", ctx);

        // ── 5. Address ────────────────────────────────────────────────────────
        text = RedactPattern(text, AddressPattern, "ADDRESS", ctx);

        // ── 6. Name (least specific — allowlist check applied) ────────────────
        if (!string.IsNullOrWhiteSpace(patientName))
            text = RedactPatientName(text, patientName, ctx);

        return (text, ctx);
    }

    /// <inheritdoc/>
    public string RestorePii(string aiResponseText, PiiRedactionContext context)
    {
        if (string.IsNullOrEmpty(aiResponseText) || !context.HasRedactions)
            return aiResponseText;

        string text = aiResponseText;

        // Longer tokens first to avoid partial substitution (e.g. [NAME_10] before [NAME_1]).
        foreach (var kv in context.Mappings
                     .OrderByDescending(m => m.Key.Length))
        {
            text = text.Replace(kv.Key, kv.Value, StringComparison.Ordinal);
        }

        return text;
    }

    /// <inheritdoc/>
    public bool ContainsPii(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;

        return SsnPattern.IsMatch(text)
            || EmailPattern.IsMatch(text)
            || PhonePattern.IsMatch(text)
            || DobPattern.IsMatch(text)
            || AddressPattern.IsMatch(text);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces all regex matches of <paramref name="pattern"/> in <paramref name="text"/>
    /// with unique placeholder tokens, recording each mapping in <paramref name="ctx"/>.
    /// </summary>
    private static string RedactPattern(
        string              text,
        Regex               pattern,
        string              category,
        PiiRedactionContext ctx)
    {
        return pattern.Replace(text, match =>
        {
            // Skip empty matches (some patterns can match zero-length strings).
            if (string.IsNullOrEmpty(match.Value)) return match.Value;
            return ctx.AddMapping(category, match.Value);
        });
    }

    /// <summary>
    /// Performs a case-sensitive exact-string replacement for a known PII value,
    /// then a case-insensitive pass to catch casing variations.
    /// Skips replacement if the value is already a placeholder token.
    /// </summary>
    private static string RedactExact(
        string              text,
        string              piiValue,
        string              category,
        PiiRedactionContext ctx)
    {
        if (string.IsNullOrWhiteSpace(piiValue)) return text;
        if (!text.Contains(piiValue, StringComparison.OrdinalIgnoreCase)) return text;

        // Only create one mapping token per unique value.
        string token = ctx.AddMapping(category, piiValue);
        return text.Replace(piiValue, token, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Redacts the patient's full name and individual name tokens from text.
    /// Tokens present in <see cref="MedicalTermAllowlist"/> are skipped to prevent
    /// false-positive redaction of medical eponyms (edge case).
    /// </summary>
    private static string RedactPatientName(
        string              text,
        string              fullName,
        PiiRedactionContext ctx)
    {
        // Redact full name first (exact phrase match, case-insensitive).
        if (text.Contains(fullName, StringComparison.OrdinalIgnoreCase))
        {
            string token = ctx.AddMapping("NAME", fullName);
            text = text.Replace(fullName, token, StringComparison.OrdinalIgnoreCase);
        }

        // Redact individual name tokens (first name, last name, middle names)
        // unless they appear in the medical terminology allowlist.
        string[] nameParts = fullName.Split(
            [' ', '-', ',', '.'],
            StringSplitOptions.RemoveEmptyEntries);

        foreach (string part in nameParts)
        {
            if (part.Length < 2) continue;
            if (MedicalTermAllowlist.Contains(part)) continue;
            if (!text.Contains(part, StringComparison.OrdinalIgnoreCase)) continue;

            string partToken = ctx.AddMapping("NAME", part);
            text = text.Replace(part, partToken, StringComparison.OrdinalIgnoreCase);
        }

        return text;
    }
}
