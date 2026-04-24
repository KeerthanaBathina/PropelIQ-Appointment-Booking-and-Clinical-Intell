using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using UPACIP.Service.Coding;

namespace UPACIP.Service.AI.Coding;

/// <summary>
/// Parses and validates the JSON tool-call response from the AI model into structured
/// <see cref="UPACIP.Service.AiCptCodeSuggestion"/> objects (US_048, AC-1, AC-2, AC-3).
///
/// Tool response format expected:
/// <code>
/// {
///   "results": [
///     {
///       "procedure_id": "guid",
///       "codes": [
///         {
///           "code_value": "99213",
///           "description": "Office visit, established patient, level 3",
///           "confidence": 0.92,
///           "justification": "...",
///           "relevance_rank": 1,
///           "is_bundled": false,
///           "bundle_components": null
///         }
///       ]
///     }
///   ]
/// }
/// </code>
///
/// Guardrails integration:
///   Each CPT code value is validated against the 5-digit numeric format.
///   Codes failing format validation, confidence range, or justification length are dropped.
/// </summary>
public sealed class CptResponseParser
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    private const float MinConfidenceThreshold = 0.01f;
    private const int   MinJustificationLength = 20;
    private const int   MaxCodeValueLength     = 10;

    /// <summary>
    /// Official CPT code format: 5 digits + optional single uppercase alphanumeric suffix.
    /// Examples: "99213", "80053", "27447", "0001F".
    /// </summary>
    private static readonly Regex CptPattern = new(
        @"^\d{5}[A-Z0-9]?$",
        RegexOptions.Compiled, TimeSpan.FromMilliseconds(50));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Dependencies
    // ─────────────────────────────────────────────────────────────────────────

    private readonly CodingGuardrailsService     _guardrails;
    private readonly ILogger<CptResponseParser>  _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public CptResponseParser(
        CodingGuardrailsService    guardrails,
        ILogger<CptResponseParser> logger)
    {
        _guardrails = guardrails;
        _logger     = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses the raw JSON tool-call arguments from the LLM into validated
    /// <see cref="UPACIP.Service.AiCptCodingResult"/> objects — one per input procedure ID.
    ///
    /// On any JSON parse failure, returns empty results for all supplied procedure IDs.
    /// </summary>
    /// <param name="rawJson">Raw JSON string from the LLM tool-call arguments.</param>
    /// <param name="procedureIds">All procedure IDs that were sent to the LLM.</param>
    /// <param name="correlationId">Correlation ID for structured logging.</param>
    public IReadOnlyList<AiCptCodingResult> Parse(
        string              rawJson,
        IReadOnlyList<Guid> procedureIds,
        Guid                correlationId)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            _logger.LogWarning(
                "CptResponseParser: received null/empty response. " +
                "Treating all procedures as uncodable. CorrelationId={CorrelationId}",
                correlationId);
            return BuildUncodableResults(procedureIds);
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
                    "CptResponseParser: unexpected response structure (not an array). " +
                    "CorrelationId={CorrelationId}", correlationId);
                return BuildUncodableResults(procedureIds);
            }

            var output = new List<AiCptCodingResult>();

            foreach (var item in resultsElement.EnumerateArray())
            {
                if (!item.TryGetProperty("procedure_id", out var procIdEl) ||
                    !Guid.TryParse(procIdEl.GetString(), out var procId))
                {
                    _logger.LogDebug(
                        "CptResponseParser: result item missing 'procedure_id'. Skipping. " +
                        "CorrelationId={CorrelationId}", correlationId);
                    continue;
                }

                if (!item.TryGetProperty("codes", out var codesEl) ||
                    codesEl.ValueKind != JsonValueKind.Array)
                {
                    output.Add(new AiCptCodingResult { ProcedureId = procId, Suggestions = [] });
                    continue;
                }

                var suggestions = new List<AiCptCodeSuggestion>();
                var rank        = 1;

                foreach (var codeEl in codesEl.EnumerateArray())
                {
                    var codeValue     = codeEl.TryGetProperty("code_value",   out var cv) ? cv.GetString() : null;
                    var description   = codeEl.TryGetProperty("description",  out var d)  ? d.GetString() ?? string.Empty : string.Empty;
                    var rawConf       = codeEl.TryGetProperty("confidence",   out var cf) ? (float)cf.GetDouble() : 0f;
                    var justification = codeEl.TryGetProperty("justification", out var j) ? j.GetString() : null;
                    var rawRank       = codeEl.TryGetProperty("relevance_rank", out var rr) ? rr.GetInt32() : rank;
                    var isBundled     = codeEl.TryGetProperty("is_bundled",   out var ib) && ib.GetBoolean();

                    IReadOnlyList<string>? bundleComponents = null;
                    if (isBundled && codeEl.TryGetProperty("bundle_components", out var bc) &&
                        bc.ValueKind == JsonValueKind.Array)
                    {
                        bundleComponents = bc.EnumerateArray()
                            .Select(e => e.GetString() ?? string.Empty)
                            .Where(s => !string.IsNullOrWhiteSpace(s))
                            .ToList();
                    }

                    var confidence = CodingGuardrailsService.CalibrateConfidence(rawConf);

                    // ── Guardrail: UNCODABLE sentinel ─────────────────────────
                    if (codeValue == "UNCODABLE")
                    {
                        suggestions.Clear();
                        break;
                    }

                    // ── Guardrail: CPT format (AIR-S02) ──────────────────────
                    if (!IsValidCptFormat(codeValue))
                    {
                        _logger.LogWarning(
                            "CptResponseParser: invalid CPT format '{Code}' — dropping. " +
                            "CorrelationId={CorrelationId}", codeValue, correlationId);
                        continue;
                    }

                    // ── Guardrail: Confidence range ───────────────────────────
                    if (!CodingGuardrailsService.IsValidConfidence(confidence)
                        || confidence < MinConfidenceThreshold)
                    {
                        _logger.LogDebug(
                            "CptResponseParser: confidence {Confidence:F2} below threshold for '{Code}'. Skipping. " +
                            "CorrelationId={CorrelationId}", confidence, codeValue, correlationId);
                        continue;
                    }

                    // ── Guardrail: Justification length (AC-2) ────────────────
                    if (!_guardrails.IsValidJustification(justification, correlationId))
                    {
                        _logger.LogDebug(
                            "CptResponseParser: justification too short or invalid for '{Code}'. Skipping. " +
                            "CorrelationId={CorrelationId}", codeValue, correlationId);
                        continue;
                    }

                    suggestions.Add(new AiCptCodeSuggestion
                    {
                        CodeValue        = codeValue!,
                        Description      = description,
                        Confidence       = confidence,
                        Justification    = justification!,
                        RelevanceRank    = rawRank,
                        IsBundled        = isBundled,
                        BundleComponents = bundleComponents,
                    });

                    rank++;
                }

                // Re-assign ranks by confidence order to ensure monotonic ranking (AC-3).
                // Bundled codes stay at rank 1 within their confidence group.
                var ranked = suggestions
                    .OrderByDescending(s => s.IsBundled)  // bundled first
                    .ThenByDescending(s => s.Confidence)
                    .Select((s, idx) => s with { RelevanceRank = idx + 1 })
                    .ToList();

                output.Add(new AiCptCodingResult
                {
                    ProcedureId = procId,
                    Suggestions = ranked,
                });
            }

            // Ensure every input procedure has a result entry.
            var coveredIds = output.Select(r => r.ProcedureId).ToHashSet();
            foreach (var id in procedureIds.Where(id => !coveredIds.Contains(id)))
            {
                output.Add(new AiCptCodingResult { ProcedureId = id, Suggestions = [] });
            }

            return output;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex,
                "CptResponseParser: JSON parse failure. Treating all procedures as uncodable. " +
                "CorrelationId={CorrelationId}", correlationId);
            return BuildUncodableResults(procedureIds);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> when <paramref name="codeValue"/> matches the CPT format
    /// (5-digit numeric + optional single alphanumeric suffix) or is the "UNCODABLE" sentinel.
    /// </summary>
    public static bool IsValidCptFormat(string? codeValue)
    {
        if (string.IsNullOrWhiteSpace(codeValue)) return false;
        if (codeValue == "UNCODABLE") return true;
        if (codeValue.Length > MaxCodeValueLength) return false;
        return CptPattern.IsMatch(codeValue);
    }

    private static IReadOnlyList<AiCptCodingResult> BuildUncodableResults(IReadOnlyList<Guid> procedureIds)
        => procedureIds
            .Select(id => new AiCptCodingResult { ProcedureId = id, Suggestions = [] })
            .ToList();
}
