using System.Text.Json;
using Microsoft.Extensions.Logging;
using UPACIP.Service.Coding;

namespace UPACIP.Service.AI.Coding;

/// <summary>
/// Parses and validates the JSON tool-call response from the AI model into
/// structured <see cref="AiCodeSuggestion"/> objects (US_047, AC-1, AC-2, AC-4).
///
/// Tool response format expected:
/// <code>
/// {
///   "results": [
///     {
///       "diagnosis_id": "guid",
///       "codes": [
///         {
///           "code_value": "J18.9",
///           "description": "Unspecified pneumonia",
///           "confidence": 0.92,
///           "justification": "...",
///           "relevance_rank": 1
///         }
///       ]
///     }
///   ]
/// }
/// </code>
///
/// Uncodable edge-case handling:
///   When no codes are returned for a diagnosis, or when the AI returns a single
///   entry with <c>code_value = "UNCODABLE"</c>, the parser emits an empty
///   <see cref="AiCodingResult.Suggestions"/> list.  The calling service then inserts
///   the "UNCODABLE" sentinel row directly.
///
/// Guardrails integration:
///   Each code is passed through <see cref="CodingGuardrailsService"/> before being
///   included in the output.  Codes that fail format validation or justification
///   length checks are dropped and logged.
/// </summary>
public sealed class Icd10ResponseParser
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    private const float MinConfidenceThreshold = 0.01f;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Dependencies
    // ─────────────────────────────────────────────────────────────────────────

    private readonly CodingGuardrailsService            _guardrails;
    private readonly ILogger<Icd10ResponseParser>        _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public Icd10ResponseParser(
        CodingGuardrailsService          guardrails,
        ILogger<Icd10ResponseParser>     logger)
    {
        _guardrails = guardrails;
        _logger     = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses the raw JSON tool-call arguments from the LLM into validated
    /// <see cref="AiCodingResult"/> objects — one per input diagnosis ID.
    ///
    /// On any JSON parse failure, returns empty results for all supplied
    /// diagnosis IDs (uncodable fallback).
    /// </summary>
    /// <param name="rawJson">Raw JSON string from the LLM tool-call arguments.</param>
    /// <param name="diagnosisIds">All diagnosis IDs that were sent to the LLM.</param>
    /// <param name="correlationId">Correlation ID for structured logging.</param>
    public IReadOnlyList<AiCodingResult> Parse(
        string              rawJson,
        IReadOnlyList<Guid> diagnosisIds,
        Guid                correlationId)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            _logger.LogWarning(
                "Icd10ResponseParser: received null/empty response. " +
                "Treating all diagnoses as uncodable. CorrelationId={CorrelationId}",
                correlationId);
            return BuildUncodableResults(diagnosisIds);
        }

        try
        {
            var doc  = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            // Support both {"results": [...]} wrapper and a top-level array.
            var resultsElement = root.ValueKind == JsonValueKind.Array
                ? root
                : root.TryGetProperty("results", out var r) ? r : root;

            if (resultsElement.ValueKind != JsonValueKind.Array)
            {
                _logger.LogWarning(
                    "Icd10ResponseParser: unexpected response structure (not an array). " +
                    "CorrelationId={CorrelationId}", correlationId);
                return BuildUncodableResults(diagnosisIds);
            }

            var output = new List<AiCodingResult>();

            foreach (var item in resultsElement.EnumerateArray())
            {
                if (!item.TryGetProperty("diagnosis_id", out var diagIdEl) ||
                    !Guid.TryParse(diagIdEl.GetString(), out var diagId))
                {
                    _logger.LogDebug(
                        "Icd10ResponseParser: result item missing 'diagnosis_id'. Skipping. " +
                        "CorrelationId={CorrelationId}", correlationId);
                    continue;
                }

                if (!item.TryGetProperty("codes", out var codesEl) ||
                    codesEl.ValueKind != JsonValueKind.Array)
                {
                    output.Add(new AiCodingResult { DiagnosisId = diagId, Suggestions = [] });
                    continue;
                }

                var suggestions = new List<AiCodeSuggestion>();
                var rank        = 1;

                foreach (var codeEl in codesEl.EnumerateArray())
                {
                    var codeValue    = codeEl.TryGetProperty("code_value", out var cv) ? cv.GetString() : null;
                    var description  = codeEl.TryGetProperty("description",  out var d)  ? d.GetString() ?? string.Empty : string.Empty;
                    var rawConf      = codeEl.TryGetProperty("confidence",   out var cf) ? (float)cf.GetDouble() : 0f;
                    var justification = codeEl.TryGetProperty("justification", out var j) ? j.GetString() : null;
                    var rawRank      = codeEl.TryGetProperty("relevance_rank", out var rr) ? rr.GetInt32() : rank;

                    var confidence = CodingGuardrailsService.CalibrateConfidence(rawConf);

                    // ── Guardrail: ICD-10 format (AIR-S02) ───────────────────
                    if (!_guardrails.IsValidIcd10Format(codeValue))
                    {
                        _logger.LogWarning(
                            "Icd10ResponseParser: invalid ICD-10 format '{Code}' — dropping. " +
                            "CorrelationId={CorrelationId}", codeValue, correlationId);
                        continue;
                    }

                    // ── Guardrail: Uncodable sentinel  ───────────────────────
                    if (codeValue == "UNCODABLE")
                    {
                        // Return empty suggestions; caller inserts sentinel.
                        suggestions.Clear();
                        break;
                    }

                    // ── Guardrail: Confidence range ──────────────────────────
                    if (!CodingGuardrailsService.IsValidConfidence(confidence)
                        || confidence < MinConfidenceThreshold)
                    {
                        _logger.LogDebug(
                            "Icd10ResponseParser: confidence {Confidence:F2} below threshold for '{Code}'. Skipping. " +
                            "CorrelationId={CorrelationId}", confidence, codeValue, correlationId);
                        continue;
                    }

                    // ── Guardrail: Justification length (AC-2) ───────────────
                    if (!_guardrails.IsValidJustification(justification, correlationId))
                    {
                        _logger.LogDebug(
                            "Icd10ResponseParser: justification too short or invalid for '{Code}'. Skipping. " +
                            "CorrelationId={CorrelationId}", codeValue, correlationId);
                        continue;
                    }

                    suggestions.Add(new AiCodeSuggestion
                    {
                        CodeValue     = codeValue!,
                        Description   = description,
                        Confidence    = confidence,
                        Justification = justification!,
                        RelevanceRank = rawRank,
                    });

                    rank++;
                }

                // Re-assign ranks by confidence order to ensure monotonic ranking (AC-4).
                var ranked = suggestions
                    .OrderByDescending(s => s.Confidence)
                    .Select((s, idx) => s with { RelevanceRank = idx + 1 })
                    .ToList();

                output.Add(new AiCodingResult
                {
                    DiagnosisId = diagId,
                    Suggestions = ranked,
                });
            }

            // Ensure every input diagnosis has a result entry.
            var coveredIds = output.Select(r => r.DiagnosisId).ToHashSet();
            foreach (var id in diagnosisIds.Where(id => !coveredIds.Contains(id)))
            {
                output.Add(new AiCodingResult { DiagnosisId = id, Suggestions = [] });
            }

            return output;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex,
                "Icd10ResponseParser: JSON parse failure. Treating all diagnoses as uncodable. " +
                "CorrelationId={CorrelationId}", correlationId);
            return BuildUncodableResults(diagnosisIds);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static IReadOnlyList<AiCodingResult> BuildUncodableResults(IReadOnlyList<Guid> diagnosisIds)
        => diagnosisIds
            .Select(id => new AiCodingResult { DiagnosisId = id, Suggestions = [] })
            .ToList();
}
