using System.Text.RegularExpressions;

namespace UPACIP.Api.Logging;

/// <summary>
/// Static masking algorithms for each PII type (US_066 AC-2, NFR-017, HIPAA Safe Harbor).
///
/// Masking patterns expose just enough identifying structure to confirm the field is present
/// while preventing reconstruction of the actual value:
///   Email:  "j***@e***.com"  — first char of local + first char of domain + TLD
///   Phone:  "***-***-1234"   — last 4 digits only
///   SSN:    "***-**-6789"    — last 4 digits only
///   Name:   "J***"           — first character only
///   DOB:    "****-01-****"   — month only (ISO 8601)
///   Generic:"[REDACTED]"     — full redaction for unrecognised PII fields
///
/// All methods are null-safe and thread-safe (pure functions, no shared state).
/// </summary>
public static class PiiMaskingPatterns
{
    // ── Pre-compiled regexes for pattern detection ────────────────────────────
    // Used by callers to choose the correct masking method when PII type is unknown.
    public static readonly Regex EmailRegex = new(
        @"^[\w._%+\-]+@[\w.\-]+\.\w{2,}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

    public static readonly Regex PhoneRegex = new(
        @"^\+?\d[\d\s()\-]{7,}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

    public static readonly Regex SsnRegex = new(
        @"^\d{3}-?\d{2}-?\d{4}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

    // ISO 8601 date pattern (YYYY-MM-DD) for DOB detection
    private static readonly Regex Iso8601DateRegex = new(
        @"^\d{4}-\d{2}-\d{2}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(50));

    // ── Masking methods ───────────────────────────────────────────────────────

    /// <summary>
    /// Masks an email address: keeps first char of local part, first char of domain, and TLD.
    /// <example>"john.doe@example.com" → "j***@e***.com"</example>
    /// </summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return string.Empty;

        var at = email.IndexOf('@');
        if (at <= 0) return MaskGeneric(email);

        var local  = email[..at];
        var domain = email[(at + 1)..];

        var dot    = domain.LastIndexOf('.');
        var tld    = dot >= 0 ? domain[dot..] : string.Empty;
        var host   = dot >= 0 ? domain[..dot] : domain;

        var maskedLocal  = local.Length >= 1 ? $"{local[0]}***" : "***";
        var maskedHost   = host.Length  >= 1 ? $"{host[0]}***"  : "***";

        return $"{maskedLocal}@{maskedHost}{tld}";
    }

    /// <summary>
    /// Masks a phone number: keeps only the last 4 digits.
    /// <example>"+1 (555) 867-5309" → "***-***-5309"</example>
    /// </summary>
    public static string MaskPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return string.Empty;

        // Extract digits only
        var digits = new string(phone.Where(char.IsDigit).ToArray());
        if (digits.Length < 4) return "***";

        var last4 = digits[^4..];
        return $"***-***-{last4}";
    }

    /// <summary>
    /// Masks a Social Security Number: keeps only the last 4 digits.
    /// <example>"123-45-6789" → "***-**-6789"</example>
    /// </summary>
    public static string MaskSsn(string? ssn)
    {
        if (string.IsNullOrWhiteSpace(ssn)) return string.Empty;

        var digits = new string(ssn.Where(char.IsDigit).ToArray());
        if (digits.Length < 4) return "***-**-****";

        var last4 = digits[^4..];
        return $"***-**-{last4}";
    }

    /// <summary>
    /// Masks a personal name: keeps only the first character.
    /// <example>"Jane Smith" → "J***"</example>
    /// </summary>
    public static string MaskName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        return $"{name[0]}***";
    }

    /// <summary>
    /// Masks a date of birth: exposes month only (ISO 8601 YYYY-MM-DD format preferred).
    /// <example>"1985-07-15" → "****-07-****"</example>
    /// Non-ISO formats: returns "****-**-****".
    /// </summary>
    public static string MaskDateOfBirth(string? dob)
    {
        if (string.IsNullOrWhiteSpace(dob)) return string.Empty;

        // ISO 8601: "YYYY-MM-DD"
        if (Iso8601DateRegex.IsMatch(dob) && dob.Length == 10)
        {
            var month = dob[5..7]; // MM
            return $"****-{month}-****";
        }

        return "****-**-****";
    }

    /// <summary>
    /// Full redaction for PII fields whose type cannot be determined from field name or value.
    /// </summary>
    public static string MaskGeneric(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return "[REDACTED]";
    }

    /// <summary>
    /// Auto-detects PII type from a string value using pattern matching
    /// and applies the appropriate masking method.
    /// Returns the original value unchanged if no pattern matches.
    /// </summary>
    public static string AutoMask(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value ?? string.Empty;

        if (EmailRegex.IsMatch(value)) return MaskEmail(value);
        if (SsnRegex.IsMatch(value))   return MaskSsn(value);
        if (PhoneRegex.IsMatch(value)) return MaskPhone(value);

        return value; // Not a recognised PII pattern — return unchanged
    }
}
