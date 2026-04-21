using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace UPACIP.Service.AI.ConversationalIntake;

/// <summary>
/// Validates and sanitises patient intake inputs and AI model responses.
///
/// Responsibilities:
///   1. <b>Input sanitisation</b>: strips prompt injection attempts from patient input
///      before it reaches the AI model (AIR-S06 — prevent prompt injection, OWASP A03).
///   2. <b>Extracted field validation</b>: checks that the AI-extracted value is
///      syntactically valid for its field type (e.g. DOB format).
///   3. <b>Confidence gate</b>: determines whether the AI's confidence score meets the
///      80% threshold (AIR-010) or whether a clarification follow-up is needed (EC-1).
///   4. <b>Max-length enforcement</b>: rejects inputs exceeding
///      <see cref="MaxPatientInputLength"/> (guardrails.json §MaxPatientInputLengthChars).
///
/// All validation failures return a structured result rather than throwing —
/// the orchestration layer decides whether to ask the patient to rephrase or to
/// fall back to the manual form.
/// </summary>
public sealed class IntakeFieldExtractionValidator
{
    // Guardrails matching guardrails.json §ConversationalIntake
    private const double ConfidenceThreshold = 0.80;
    private const int    MaxPatientInputLength = 1_000;

    /// <summary>
    /// Regex-based prompt injection detection patterns (AIR-S06 / OWASP A03).
    /// Uses compiled regex for performance under request load.
    /// Case-insensitive, matching anywhere in the input.
    /// </summary>
    private static readonly Regex InjectionPattern = new(
        @"ignore\s+previous\s+instructions|disregard\s+above|system\s*prompt|jailbreak|act\s+as\s+|you\s+are\s+now\s+|<\/system>|<\|im_end\|>|DAN\s+mode|developer\s+mode",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(50));

    /// <summary>
    /// Date-of-birth pattern: MM/DD/YYYY (strict — AI should have normalised the input).
    /// </summary>
    private static readonly Regex DobPattern = new(
        @"^\d{2}/\d{2}/\d{4}$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(10));

    /// <summary>
    /// Phone pattern: accepts common North American and international formats.
    /// </summary>
    private static readonly Regex PhonePattern = new(
        @"^[\+]?[\d\s\(\)\-\.]{7,20}$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(10));

    /// <summary>Basic email pattern (non-strict — full validation happens in registration).</summary>
    private static readonly Regex EmailPattern = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(10));

    private readonly ILogger<IntakeFieldExtractionValidator> _logger;

    public IntakeFieldExtractionValidator(ILogger<IntakeFieldExtractionValidator> logger)
    {
        _logger = logger;
    }

    // ─── Input sanitisation (AIR-S06) ─────────────────────────────────────────

    /// <summary>
    /// Sanitises patient input before sending to the AI model.
    /// Returns a <see cref="SanitisedInput"/> with an <see cref="SanitisedInput.IsBlocked"/>
    /// flag when a prompt injection is detected.
    /// </summary>
    public SanitisedInput SanitisePatientInput(string rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
            return new SanitisedInput { Value = string.Empty, IsBlocked = false };

        // Enforce maximum length (guardrails.json)
        if (rawInput.Length > MaxPatientInputLength)
        {
            rawInput = rawInput[..MaxPatientInputLength];
        }

        // Strip null bytes and non-printable control characters (OWASP A03)
        var sanitised = Regex.Replace(rawInput, @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", string.Empty);

        // Detect prompt injection keywords (AIR-S06) — block the request
        if (InjectionPattern.IsMatch(sanitised))
        {
            _logger.LogWarning(
                "IntakeValidator: prompt injection pattern detected in patient input (sessionId redacted).");
            return new SanitisedInput { Value = sanitised, IsBlocked = true };
        }

        return new SanitisedInput { Value = sanitised.Trim(), IsBlocked = false };
    }

    // ─── Extracted field validation ────────────────────────────────────────────

    /// <summary>
    /// Validates the AI-extracted value for the given <paramref name="fieldKey"/>.
    /// Returns a <see cref="FieldValidationResult"/> describing whether the value is
    /// acceptable or whether clarification is needed (EC-1).
    /// </summary>
    public FieldValidationResult ValidateExtractedField(
        string fieldKey,
        string? extractedValue,
        double confidence)
    {
        // Confidence gate (AIR-010): below threshold → clarification required (EC-1)
        if (confidence < ConfidenceThreshold)
        {
            _logger.LogDebug(
                "IntakeValidator: low confidence {Confidence:F2} for field={FieldKey}; clarification needed.",
                confidence, fieldKey);
            return FieldValidationResult.NeedsClarification(
                $"The confidence score {confidence:P0} is below the {ConfidenceThreshold:P0} threshold.");
        }

        if (string.IsNullOrWhiteSpace(extractedValue))
        {
            return FieldValidationResult.NeedsClarification("No value was extracted from the patient's response.");
        }

        // Field-type format validation
        var formatError = ValidateFieldFormat(fieldKey, extractedValue);
        if (formatError is not null)
        {
            return FieldValidationResult.NeedsClarification(formatError);
        }

        return FieldValidationResult.Valid(extractedValue);
    }

    // ─── Private helpers ───────────────────────────────────────────────────────

    private static string? ValidateFieldFormat(string fieldKey, string value)
    {
        return fieldKey switch
        {
            IntakeFieldDefinitions.DateOfBirth when !DobPattern.IsMatch(value) =>
                "Please provide your date of birth in MM/DD/YYYY format (e.g. 01/15/1985).",
            IntakeFieldDefinitions.ContactPhone when !PhonePattern.IsMatch(value) =>
                "Please provide a valid phone number (e.g. (555) 123-4567).",
            IntakeFieldDefinitions.EmergencyContactPhone when !PhonePattern.IsMatch(value) =>
                "Please provide a valid emergency contact phone number.",
            IntakeFieldDefinitions.ContactEmail when !EmailPattern.IsMatch(value) =>
                "Please provide a valid email address (e.g. name@example.com).",
            _ => null,
        };
    }
}

// ─── Result types ──────────────────────────────────────────────────────────────

/// <summary>Result of sanitising patient input (AIR-S06).</summary>
public sealed class SanitisedInput
{
    public required string Value { get; init; }
    /// <summary>True when a prompt injection was detected — the input must not be sent to the model.</summary>
    public bool IsBlocked { get; init; }
}

/// <summary>Result of validating an AI-extracted field value.</summary>
public sealed class FieldValidationResult
{
    public bool IsValid { get; private init; }
    public bool RequiresClarification { get; private init; }
    public string? CleanValue { get; private init; }
    public string? Reason { get; private init; }

    public static FieldValidationResult Valid(string cleanValue) =>
        new() { IsValid = true, CleanValue = cleanValue };

    public static FieldValidationResult NeedsClarification(string reason) =>
        new() { IsValid = false, RequiresClarification = true, Reason = reason };
}
