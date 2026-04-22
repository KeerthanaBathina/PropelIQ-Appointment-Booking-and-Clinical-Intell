using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace UPACIP.Service.AI.DocumentParsing;

/// <summary>
/// Result envelope returned after AI model invocation (US_039 AC-3).
/// </summary>
public sealed class DocumentParsingModelResult
{
    public bool ExtractionPossible { get; init; }
    public double Confidence       { get; init; }
    public string? DocumentDate    { get; init; }
    public string? ProviderName    { get; init; }
    public string? Summary         { get; init; }
    public IReadOnlyDictionary<string, string?> ExtractedFields { get; init; }
        = new Dictionary<string, string?>();
    public string? ManualReviewReason { get; init; }
}

/// <summary>
/// Validates AI parsing responses and sanitises document text before model invocation
/// (US_039 AC-2, AC-3, AC-5; AIR-S01, AIR-O01, AIR-O07, AIR-O08).
///
/// Responsibilities:
///   1. <b>Input sanitisation</b>: strip potential SSN-like patterns and prompt-injection
///      keywords from document content before it is sent to the AI model (AIR-S01).
///   2. <b>Token budget enforcement</b>: truncate document text to the maximum allowed
///      character count before prompt assembly (AIR-O01).
///   3. <b>Output shape validation</b>: verify the model's tool-call argument JSON
///      matches the expected extraction schema (AIR-O07).
///   4. <b>Confidence gate</b>: flag low-confidence extractions for manual review (AIR-O08).
/// </summary>
public sealed class DocumentParsingResultValidator
{
    // Guardrails matching guardrails.json §DocumentParsing
    internal const double ConfidenceThreshold = 0.70;

    /// <summary>Regex matching SSN-like patterns for redaction (AIR-S01).</summary>
    private static readonly Regex SsnPattern = new(
        @"\b\d{3}-\d{2}-\d{4}\b|\b\d{9}\b",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));

    /// <summary>Prompt injection keywords that should not reach the model (AIR-S06).</summary>
    private static readonly string[] InjectionKeywords =
    [
        "ignore previous instructions",
        "disregard above",
        "system prompt",
        "jailbreak",
        "act as",
        "you are now",
        "</system>",
        "<|im_end|>",
        "DAN mode",
        "developer mode",
        "bypass guardrails",
        "override safety",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger<DocumentParsingResultValidator> _logger;

    public DocumentParsingResultValidator(ILogger<DocumentParsingResultValidator> logger)
    {
        _logger = logger;
    }

    // ─── Input sanitisation (AIR-S01, AIR-S06) ────────────────────────────────────

    /// <summary>
    /// Sanitises raw document text before it is embedded in the AI prompt.
    /// Returns the sanitised text (may be truncated).
    /// </summary>
    public string SanitiseDocumentContent(string rawText, Guid documentId)
    {
        if (string.IsNullOrWhiteSpace(rawText))
            return string.Empty;

        // Truncate to token budget (AIR-O01).
        var text = rawText.Length > DocumentParsingPromptBuilder.MaxDocumentContentChars
            ? rawText[..DocumentParsingPromptBuilder.MaxDocumentContentChars]
            : rawText;

        // Redact SSN-like patterns (AIR-S01, OWASP A02).
        text = SsnPattern.Replace(text, "[REDACTED]");

        // Block prompt injection keywords (AIR-S06, OWASP A03).
        bool injectionDetected = false;
        foreach (var keyword in InjectionKeywords)
        {
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                text = text.Replace(keyword, "[BLOCKED]", StringComparison.OrdinalIgnoreCase);
                injectionDetected = true;
            }
        }

        if (injectionDetected)
        {
            _logger.LogWarning(
                "DocumentParsingResultValidator: prompt injection keyword detected in document content. " +
                "DocumentId={DocumentId}", documentId);
        }

        return text;
    }

    // ─── Output validation (AIR-O07, AIR-O08) ─────────────────────────────────────

    /// <summary>
    /// Parses and validates the JSON arguments from the model's tool-call response.
    /// Returns <c>null</c> and logs an error if the response is structurally invalid (AIR-O07).
    /// </summary>
    public DocumentParsingModelResult? ValidateToolCallArguments(
        string argumentsJson,
        Guid   documentId)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            _logger.LogError(
                "DocumentParsingResultValidator: empty tool-call arguments. DocumentId={DocumentId}",
                documentId);
            return null;
        }

        try
        {
            var root = JsonDocument.Parse(argumentsJson).RootElement;

            // Required fields.
            if (!root.TryGetProperty("extraction_possible", out var epProp) ||
                epProp.ValueKind != JsonValueKind.True && epProp.ValueKind != JsonValueKind.False)
            {
                _logger.LogError(
                    "DocumentParsingResultValidator: missing extraction_possible. DocumentId={DocumentId}",
                    documentId);
                return null;
            }

            if (!root.TryGetProperty("confidence", out var confProp) ||
                confProp.ValueKind != JsonValueKind.Number)
            {
                _logger.LogError(
                    "DocumentParsingResultValidator: missing confidence. DocumentId={DocumentId}",
                    documentId);
                return null;
            }

            var extractionPossible = epProp.GetBoolean();
            var confidence         = confProp.GetDouble();

            // Clamp to [0, 1] (AIR-O08).
            confidence = Math.Clamp(confidence, 0.0, 1.0);

            root.TryGetProperty("extracted_fields", out var fieldsProp);
            var fields = ParseExtractedFields(fieldsProp, documentId);

            root.TryGetProperty("document_date",       out var dateProp);
            root.TryGetProperty("provider_name",       out var provProp);
            root.TryGetProperty("summary",             out var sumProp);
            root.TryGetProperty("manual_review_reason", out var mrrProp);

            var result = new DocumentParsingModelResult
            {
                ExtractionPossible = extractionPossible,
                Confidence         = confidence,
                DocumentDate       = dateProp.ValueKind == JsonValueKind.String ? dateProp.GetString() : null,
                ProviderName       = provProp.ValueKind == JsonValueKind.String ? provProp.GetString() : null,
                Summary            = sumProp.ValueKind  == JsonValueKind.String ? sumProp.GetString()  : null,
                ExtractedFields    = fields,
                ManualReviewReason = mrrProp.ValueKind  == JsonValueKind.String ? mrrProp.GetString()  : null,
            };

            // Log confidence gate outcome without logging field values (PII guard, AIR-S01).
            if (confidence < ConfidenceThreshold)
            {
                _logger.LogWarning(
                    "DocumentParsingResultValidator: low confidence {Confidence:F2} < {Threshold:F2}. " +
                    "DocumentId={DocumentId} ManualReview={Reason}",
                    confidence, ConfidenceThreshold, documentId, result.ManualReviewReason ?? "none");
            }
            else
            {
                _logger.LogInformation(
                    "DocumentParsingResultValidator: validated successfully. " +
                    "DocumentId={DocumentId} Confidence={Confidence:F2} Fields={FieldCount}",
                    documentId, confidence, fields.Count);
            }

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex,
                "DocumentParsingResultValidator: failed to parse tool-call JSON. DocumentId={DocumentId}",
                documentId);
            return null;
        }
    }

    // ─── Private helpers ───────────────────────────────────────────────────────────

    private IReadOnlyDictionary<string, string?> ParseExtractedFields(
        JsonElement fieldsProp, Guid documentId)
    {
        var dict = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        if (fieldsProp.ValueKind != JsonValueKind.Object)
            return dict;

        foreach (var property in fieldsProp.EnumerateObject())
        {
            var key   = property.Name;
            var value = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString()
                : null;

            // Truncate runaway field values (AIR-O07 — prevent large single-field payloads).
            if (value?.Length > 500)
            {
                value = value[..500];
                _logger.LogDebug(
                    "DocumentParsingResultValidator: field '{Key}' truncated to 500 chars. DocumentId={DocumentId}",
                    key, documentId);
            }

            dict[key] = value;
        }

        return dict;
    }
}
