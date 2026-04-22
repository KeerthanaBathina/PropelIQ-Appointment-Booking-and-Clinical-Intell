using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess.Enums;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Entities.OwnedTypes;
using ReviewReasonEnum = UPACIP.DataAccess.Enums.ReviewReason;

namespace UPACIP.Service.AI.ClinicalExtraction;

// ─── Result model ─────────────────────────────────────────────────────────────

/// <summary>Outcome classifications returned by the clinical extraction model (US_040 EC-1, EC-2).</summary>
public enum ExtractionOutcome
{
    Extracted,
    NoDataExtracted,
    UnsupportedLanguage,
    InvalidResponse,
}

/// <summary>Single extracted clinical entity with per-item confidence and review metadata (US_041 AC-1, AC-2, EC-1, EC-2).</summary>
public sealed class ClinicalExtractedItem
{
    public DataType DataType    { get; init; }

    /// <summary>Per-item confidence score in [0.0, 1.0]. Defaults to 0.00 when the model omits it (EC-1).</summary>
    public double   Confidence  { get; init; }

    /// <summary>True when the model did not return a confidence value and the default 0.00 was applied (EC-1).</summary>
    public bool     ConfidenceUnavailable { get; init; }

    /// <summary>True when this item must be manually verified by staff (AC-2, EC-1).</summary>
    public bool     FlaggedForReview { get; init; }

    /// <summary>Structured reason the item was placed in mandatory review (EC-1, EC-2).</summary>
    public ReviewReasonEnum ReviewReason { get; init; } = ReviewReasonEnum.None;

    public int      PageNumber  { get; init; } = 1;
    public string   ExtractionRegion { get; init; } = string.Empty;

    /// <summary>Ready-to-persist content mapped to <see cref="ExtractedDataContent"/>.</summary>
    public ExtractedDataContent Content { get; init; } = new();
}

/// <summary>Normalized result envelope returned by <see cref="ClinicalExtractionResultValidator"/>.</summary>
public sealed class ClinicalExtractionResult
{
    public ExtractionOutcome Outcome      { get; init; }
    public double            Confidence   { get; init; }
    public string?           OutcomeReason { get; init; }
    public IReadOnlyList<ClinicalExtractedItem> Items { get; init; } = [];
}

// ─── Validator ────────────────────────────────────────────────────────────────

/// <summary>
/// Parses and validates the <c>extract_clinical_data</c> tool-call response from the AI model,
/// normalises the structured output into <see cref="ClinicalExtractionResult"/>, and sanitises
/// field values before they are handed to the persistence layer (US_040 AC-1–AC-5, EC-1, EC-2;
/// AIR-S01, AIR-O01, AIR-O08).
///
/// Responsibilities:
///   1. Parse the JSON tool-call arguments and validate required fields.
///   2. Normalise <c>extraction_outcome</c> to <see cref="ExtractionOutcome"/> enum.
///   3. Per-item: truncate runaway strings, strip SSN-like PII, map to <see cref="ExtractedDataContent"/>.
///   4. Confidence gate: flag items below threshold for manual review (AIR-O08).
/// </summary>
public sealed class ClinicalExtractionResultValidator
{
    // Threshold below which items are flagged for mandatory staff review (US_041 AC-2).
    internal const double ConfidenceThreshold = 0.80;
    private const  int    MaxFieldValueChars  = 500;

    private static readonly Regex SsnPattern = new(
        @"\b\d{3}-\d{2}-\d{4}\b|\b\d{9}\b",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ILogger<ClinicalExtractionResultValidator> _logger;

    public ClinicalExtractionResultValidator(ILogger<ClinicalExtractionResultValidator> logger)
    {
        _logger = logger;
    }

    // ─── Main entry point ─────────────────────────────────────────────────────────

    /// <summary>
    /// Validates the tool-call JSON arguments from the model and returns a normalised result.
    /// Returns <see cref="ExtractionOutcome.InvalidResponse"/> on parse failure (AIR-O07).
    /// </summary>
    public ClinicalExtractionResult Validate(string argumentsJson, Guid documentId)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            _logger.LogError(
                "ClinicalExtractionResultValidator: empty tool-call arguments. DocumentId={DocumentId}",
                documentId);
            return Invalid(documentId, "empty response");
        }

        try
        {
            var root = JsonDocument.Parse(argumentsJson).RootElement;

            // ── extraction_outcome (required) ────────────────────────────────────
            if (!root.TryGetProperty("extraction_outcome", out var outcomeProp) ||
                outcomeProp.ValueKind != JsonValueKind.String)
            {
                _logger.LogError(
                    "ClinicalExtractionResultValidator: missing extraction_outcome. DocumentId={DocumentId}",
                    documentId);
                return Invalid(documentId, "missing extraction_outcome");
            }

            var outcomeRaw = outcomeProp.GetString() ?? string.Empty;
            var outcome    = ParseOutcome(outcomeRaw);

            // ── confidence (required) ────────────────────────────────────────────
            var confidence = root.TryGetProperty("confidence", out var confProp) &&
                             confProp.ValueKind == JsonValueKind.Number
                ? Math.Clamp(confProp.GetDouble(), 0.0, 1.0)
                : 0.0;

            root.TryGetProperty("outcome_reason", out var reasonProp);
            var reason = reasonProp.ValueKind == JsonValueKind.String ? reasonProp.GetString() : null;

            // ── Non-extracted outcomes — short-circuit ───────────────────────────
            if (outcome != ExtractionOutcome.Extracted)
            {
                _logger.LogInformation(
                    "ClinicalExtractionResultValidator: non-extracted outcome {Outcome}. " +
                    "DocumentId={DocumentId} Reason={Reason}",
                    outcome, documentId, reason ?? "none");

                return new ClinicalExtractionResult
                {
                    Outcome       = outcome,
                    Confidence    = confidence,
                    OutcomeReason = reason,
                    Items         = [],
                };
            }

            // ── Parse item arrays ────────────────────────────────────────────────
            var items = new List<ClinicalExtractedItem>();

            if (root.TryGetProperty("medications", out var medsEl))
                items.AddRange(ParseMedications(medsEl, documentId));

            if (root.TryGetProperty("diagnoses", out var dxEl))
                items.AddRange(ParseDiagnoses(dxEl, documentId));

            if (root.TryGetProperty("procedures", out var procEl))
                items.AddRange(ParseProcedures(procEl, documentId));

            if (root.TryGetProperty("allergies", out var allergyEl))
                items.AddRange(ParseAllergies(allergyEl, documentId));

            _logger.LogInformation(
                "ClinicalExtractionResultValidator: validated successfully. " +
                "DocumentId={DocumentId} Confidence={Confidence:F2} Items={Count}",
                documentId, confidence, items.Count);

            return new ClinicalExtractionResult
            {
                Outcome       = ExtractionOutcome.Extracted,
                Confidence    = confidence,
                OutcomeReason = null,
                Items         = items,
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex,
                "ClinicalExtractionResultValidator: JSON parse failure. DocumentId={DocumentId}",
                documentId);
            return Invalid(documentId, "JSON parse error");
        }
    }

    // ─── Item parsers ─────────────────────────────────────────────────────────────

    private IEnumerable<ClinicalExtractedItem> ParseMedications(JsonElement arr, Guid documentId)
    {
        if (arr.ValueKind != JsonValueKind.Array) yield break;

        foreach (var el in arr.EnumerateArray())
        {
            var drugName = SafeString(el, "drug_name");
            if (string.IsNullOrWhiteSpace(drugName)) continue;

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AddIfPresent(metadata, el, "dosage");
            AddIfPresent(metadata, el, "frequency");
            AddIfPresent(metadata, el, "route");
            AddIfPresent(metadata, el, "prescribing_physician");
            AddIfPresent(metadata, el, "prescription_date");

            yield return BuildItem(DataType.Medication, el, documentId,
                normalizedValue: drugName,
                rawText:         SafeString(el, "raw_text"),
                metadata:        metadata);
        }
    }

    private IEnumerable<ClinicalExtractedItem> ParseDiagnoses(JsonElement arr, Guid documentId)
    {
        if (arr.ValueKind != JsonValueKind.Array) yield break;

        foreach (var el in arr.EnumerateArray())
        {
            var conditionName = SafeString(el, "condition_name");
            if (string.IsNullOrWhiteSpace(conditionName)) continue;

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AddIfPresent(metadata, el, "icd_code");
            AddIfPresent(metadata, el, "diagnosis_date");
            AddIfPresent(metadata, el, "treating_provider");
            AddIfPresent(metadata, el, "status");

            yield return BuildItem(DataType.Diagnosis, el, documentId,
                normalizedValue: conditionName,
                rawText:         SafeString(el, "raw_text"),
                metadata:        metadata);
        }
    }

    private IEnumerable<ClinicalExtractedItem> ParseProcedures(JsonElement arr, Guid documentId)
    {
        if (arr.ValueKind != JsonValueKind.Array) yield break;

        foreach (var el in arr.EnumerateArray())
        {
            var procedureName = SafeString(el, "procedure_name");
            if (string.IsNullOrWhiteSpace(procedureName)) continue;

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AddIfPresent(metadata, el, "procedure_date");
            AddIfPresent(metadata, el, "performing_physician");
            AddIfPresent(metadata, el, "facility");
            AddIfPresent(metadata, el, "cpt_code");

            yield return BuildItem(DataType.Procedure, el, documentId,
                normalizedValue: procedureName,
                rawText:         SafeString(el, "raw_text"),
                metadata:        metadata);
        }
    }

    private IEnumerable<ClinicalExtractedItem> ParseAllergies(JsonElement arr, Guid documentId)
    {
        if (arr.ValueKind != JsonValueKind.Array) yield break;

        foreach (var el in arr.EnumerateArray())
        {
            var allergen = SafeString(el, "allergen");
            if (string.IsNullOrWhiteSpace(allergen)) continue;

            var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            AddIfPresent(metadata, el, "reaction_type");
            AddIfPresent(metadata, el, "severity");
            AddIfPresent(metadata, el, "onset_date");
            AddIfPresent(metadata, el, "status");

            yield return BuildItem(DataType.Allergy, el, documentId,
                normalizedValue: allergen,
                rawText:         SafeString(el, "raw_text"),
                metadata:        metadata);
        }
    }

    // ─── Private helpers ──────────────────────────────────────────────────────────

    private ClinicalExtractedItem BuildItem(
        DataType   dataType,
        JsonElement el,
        Guid       documentId,
        string?    normalizedValue,
        string?    rawText,
        Dictionary<string, string> metadata)
    {
        var pageNumber = el.TryGetProperty("page_number", out var pgProp) &&
                         pgProp.ValueKind == JsonValueKind.Number
            ? Math.Max(1, pgProp.GetInt32())
            : 1;

        var region = SafeString(el, "extraction_region") ?? "unknown";

        // ── Per-item confidence (AC-1, EC-1, EC-2) ────────────────────────────
        // Read the item-level confidence field. When absent or null, default to 0.0
        // and mark the item as confidence-unavailable (EC-1: requires manual review).
        bool confidenceUnavailable;
        double itemConfidence;

        if (el.TryGetProperty("confidence", out var itemConfProp) &&
            itemConfProp.ValueKind == JsonValueKind.Number)
        {
            itemConfidence       = Math.Clamp(itemConfProp.GetDouble(), 0.0, 1.0);
            confidenceUnavailable = false;
        }
        else
        {
            itemConfidence       = 0.0;
            confidenceUnavailable = true;

            _logger.LogWarning(
                "ClinicalExtractionResultValidator: item missing confidence. " +
                "DataType={DataType} DocumentId={DocumentId} — defaulting to 0.00 (confidence-unavailable).",
                dataType, documentId);
        }

        var flaggedForReview = confidenceUnavailable || itemConfidence < ConfidenceThreshold;
        var reviewReason = confidenceUnavailable
            ? ReviewReasonEnum.ConfidenceUnavailable
            : (itemConfidence < ConfidenceThreshold ? ReviewReasonEnum.LowConfidence : ReviewReasonEnum.None);

        return new ClinicalExtractedItem
        {
            DataType              = dataType,
            Confidence            = itemConfidence,
            ConfidenceUnavailable = confidenceUnavailable,
            FlaggedForReview      = flaggedForReview,
            ReviewReason          = reviewReason,
            PageNumber            = pageNumber,
            ExtractionRegion      = Truncate(region, 200) ?? "unknown",
            Content               = new ExtractedDataContent
            {
                NormalizedValue = Redact(Truncate(normalizedValue, MaxFieldValueChars)),
                RawText         = Redact(Truncate(rawText, MaxFieldValueChars)),
                Metadata        = metadata.ToDictionary(
                    kv => kv.Key,
                    kv => Redact(Truncate(kv.Value, MaxFieldValueChars)) ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase),
            },
        };
    }

    private static ExtractionOutcome ParseOutcome(string raw) => raw.ToLowerInvariant() switch
    {
        "extracted"          => ExtractionOutcome.Extracted,
        "no-data-extracted"  => ExtractionOutcome.NoDataExtracted,
        "unsupported-language" => ExtractionOutcome.UnsupportedLanguage,
        _                    => ExtractionOutcome.InvalidResponse,
    };

    private static ClinicalExtractionResult Invalid(Guid _, string reason) =>
        new() { Outcome = ExtractionOutcome.InvalidResponse, Confidence = 0, OutcomeReason = reason, Items = [] };

    private static string? SafeString(JsonElement el, string key) =>
        el.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    private static void AddIfPresent(Dictionary<string, string> dict, JsonElement el, string key)
    {
        var val = SafeString(el, key);
        if (!string.IsNullOrWhiteSpace(val))
            dict[key] = val;
    }

    private static string? Truncate(string? s, int max) =>
        s is null ? null : s.Length > max ? s[..max] : s;

    private static string? Redact(string? s) =>
        s is null ? null : SsnPattern.Replace(s, "[REDACTED]");
}
