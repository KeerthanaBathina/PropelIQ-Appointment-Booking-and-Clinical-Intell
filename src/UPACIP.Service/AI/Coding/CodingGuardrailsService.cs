using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;

namespace UPACIP.Service.AI.Coding;

/// <summary>
/// Input sanitisation and output validation guardrails for the ICD-10 coding AI pipeline
/// (US_047, AIR-S01, AIR-S02, AIR-S05, AIR-Q06).
///
/// Responsibilities:
/// <list type="bullet">
///   <item>
///     <strong>PII redaction</strong>: scrub SSN patterns, email addresses, and
///     injection keywords from diagnosis text before it reaches an external API (AIR-S01).
///   </item>
///   <item>
///     <strong>ICD-10 format validation</strong>: verify each suggested code value matches
///     the official ICD-10-CM format regex (AIR-S02).
///   </item>
///   <item>
///     <strong>Code library cross-reference</strong>: confirm each validated code is present
///     and current in the <c>icd10_code_library</c> table (AIR-S02, DR-015).
///   </item>
///   <item>
///     <strong>Confidence range validation</strong>: reject scores outside [0.0, 1.0].
///   </item>
///   <item>
///     <strong>Justification length check</strong>: require at least 20 characters to
///     filter boilerplate or empty justification strings (AC-2).
///   </item>
///   <item>
///     <strong>Content filter</strong>: reject responses containing injection keywords or
///     inappropriate content (AIR-S05).
///   </item>
/// </list>
/// </summary>
public sealed class CodingGuardrailsService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants (must match coding-guardrails.json)
    // ─────────────────────────────────────────────────────────────────────────

    private const int MinJustificationLength = 20;
    private const int MaxCodeValueLength     = 10;

    /// <summary>
    /// Official ICD-10-CM code format: uppercase letter + 2 digits + optional period +
    /// optional 1–4 alphanumeric chars.  Examples: "J18.9", "E11.65", "K92.1", "Z87.391".
    /// </summary>
    private static readonly Regex Icd10Pattern = new(
        @"^[A-Z]\d{2}(\.\d{1,4})?$",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));

    // PII patterns (AIR-S01)
    private static readonly Regex SsnPattern = new(
        @"\b\d{3}-\d{2}-\d{4}\b|\b\d{9}\b",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));

    private static readonly Regex EmailPattern = new(
        @"\b[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}\b",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));

    // Prompt injection keywords (AIR-S05)
    private static readonly string[] InjectionKeywords =
    [
        "ignore previous instructions", "disregard above", "system prompt",
        "jailbreak", "act as", "you are now", "</system>", "<|im_end|>",
        "DAN mode", "developer mode", "bypass guardrails", "override safety",
    ];

    // ─────────────────────────────────────────────────────────────────────────
    // Dependencies
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ApplicationDbContext               _db;
    private readonly ILogger<CodingGuardrailsService>  _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public CodingGuardrailsService(
        ApplicationDbContext              db,
        ILogger<CodingGuardrailsService>  logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Input sanitisation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Redacts SSN patterns, email addresses, and blocks injection keywords from
    /// the given text before it is included in an AI prompt (AIR-S01).
    ///
    /// Returns <c>null</c> when the text contains injection keywords — the caller
    /// should treat this diagnosis as uncodable rather than passing it to the LLM.
    /// </summary>
    /// <param name="text">Raw diagnosis text from <c>ExtractedDataContent</c>.</param>
    /// <param name="correlationId">Correlation ID for audit logging.</param>
    public string? SanitiseInput(string? text, Guid correlationId)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // Block prompt injection attempts.
        foreach (var keyword in InjectionKeywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "CodingGuardrails: injection keyword '{Keyword}' detected in diagnosis text. " +
                    "Rejecting input. CorrelationId={CorrelationId}",
                    keyword, correlationId);
                return null; // signal to treat as uncodable
            }
        }

        // Redact PII patterns.
        var sanitised = SsnPattern.Replace(text, "[REDACTED]");
        sanitised     = EmailPattern.Replace(sanitised, "[REDACTED_EMAIL]");

        return sanitised;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Output validation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when <paramref name="codeValue"/> matches the ICD-10-CM
    /// alphanumeric format pattern (AIR-S02).  The sentinel value <c>"UNCODABLE"</c>
    /// is accepted as a valid special case.
    /// </summary>
    public bool IsValidIcd10Format(string? codeValue)
    {
        if (string.IsNullOrWhiteSpace(codeValue)) return false;
        if (codeValue == "UNCODABLE") return true;
        if (codeValue.Length > MaxCodeValueLength) return false;
        return Icd10Pattern.IsMatch(codeValue);
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="confidence"/> is within [0.0, 1.0].
    /// LLM responses sometimes return scores on a 0–100 scale; those are treated as
    /// invalid and normalised by the caller before this check.
    /// </summary>
    public static bool IsValidConfidence(float confidence)
        => confidence is >= 0.0f and <= 1.0f;

    /// <summary>
    /// Returns <c>true</c> when <paramref name="justification"/> meets the minimum
    /// length requirement (AC-2) and does not contain injection keywords (AIR-S05).
    /// </summary>
    public bool IsValidJustification(string? justification, Guid correlationId)
    {
        if (string.IsNullOrWhiteSpace(justification)) return false;
        if (justification.Length < MinJustificationLength) return false;

        foreach (var keyword in InjectionKeywords)
        {
            if (!justification.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                continue;

            _logger.LogWarning(
                "CodingGuardrails: injection keyword '{Keyword}' in AI justification. " +
                "CorrelationId={CorrelationId}", keyword, correlationId);
            return false;
        }

        return true;
    }

    /// <summary>
    /// Returns <c>true</c> when <paramref name="codeValue"/> exists in the current
    /// <c>icd10_code_library</c> (is_current = true) or is the "UNCODABLE" sentinel (AIR-S02, DR-015).
    ///
    /// Cross-references against the live database so that codes deprecated since the
    /// last library refresh are rejected immediately.
    /// </summary>
    public async Task<bool> IsCurrentLibraryCodeAsync(string codeValue, CancellationToken ct)
    {
        if (codeValue == "UNCODABLE") return true;

        return await _db.Icd10CodeLibrary
            .AnyAsync(l => l.CodeValue == codeValue && l.IsCurrent, ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Confidence calibration helper
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Normalises a raw confidence score to [0.0, 1.0].
    /// LLMs sometimes return scores on a 0–100 scale; values > 1 are divided by 100.
    /// </summary>
    public static float CalibrateConfidence(float raw)
        => raw > 1f ? raw / 100f : raw;
}
