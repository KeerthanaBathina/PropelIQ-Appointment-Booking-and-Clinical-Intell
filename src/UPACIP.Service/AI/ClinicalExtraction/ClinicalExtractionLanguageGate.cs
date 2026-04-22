using Microsoft.Extensions.Logging;

namespace UPACIP.Service.AI.ClinicalExtraction;

/// <summary>
/// Lightweight language gate that detects non-English documents before any external model
/// invocation (US_040 EC-2; AIR-S01).
///
/// Phase 1 supports English only. Detection uses a heuristic based on the ratio of
/// ASCII printable characters to total characters, supplemented by a small set of
/// common English function words. This avoids adding an NLP dependency while reliably
/// flagging documents that are predominantly non-Latin-script (e.g. Chinese, Arabic, Cyrillic).
///
/// Intentional limitations (accepted for Phase 1):
/// - European languages with Latin scripts may pass the ASCII check even though they are
///   not English. The function-word check provides a secondary signal.
/// - Very short documents (&#60;20 tokens) may produce unreliable ratios; they are passed through.
/// </summary>
public sealed class ClinicalExtractionLanguageGate
{
    // Guardrail: minimum fraction of ASCII printable characters for "likely English" (guardrails.json).
    internal const double MinEnglishCharRatio = 0.60;

    // Short-document threshold: bypass language check for tiny inputs (single line, header-only docs).
    private const int MinCharsForCheck = 100;

    // Common English function words used as secondary signal.
    private static readonly string[] EnglishFunctionWords =
    [
        " the ", " and ", " of ", " is ", " in ", " to ", " a ", " an ",
        " for ", " with ", " on ", " at ", " by ", " from ", " that ", " this ",
        " was ", " are ", " has ", " have ", " patient ", " date ", " mg ",
    ];

    private readonly ILogger<ClinicalExtractionLanguageGate> _logger;

    public ClinicalExtractionLanguageGate(ILogger<ClinicalExtractionLanguageGate> logger)
    {
        _logger = logger;
    }

    // ─── Public API ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if the document text is likely English and safe to send for extraction.
    /// Returns <c>false</c> if the document appears to be in an unsupported language (EC-2).
    /// </summary>
    public bool IsEnglish(string documentText, Guid documentId)
    {
        if (string.IsNullOrWhiteSpace(documentText))
        {
            // Empty or whitespace-only content: let the extraction service handle it as no-data.
            return true;
        }

        // Short documents: skip the ratio check to avoid false negatives (e.g. single-line headers).
        if (documentText.Length < MinCharsForCheck)
        {
            _logger.LogDebug(
                "ClinicalExtractionLanguageGate: document too short for language check; passing through. " +
                "DocumentId={DocumentId} Length={Length}",
                documentId, documentText.Length);
            return true;
        }

        // ── ASCII printable ratio check ─────────────────────────────────────────
        int asciiCount = 0;
        foreach (var c in documentText)
        {
            if (c >= 0x20 && c <= 0x7E) asciiCount++;
        }

        var ratio = (double)asciiCount / documentText.Length;
        if (ratio < MinEnglishCharRatio)
        {
            _logger.LogWarning(
                "ClinicalExtractionLanguageGate: low ASCII ratio {Ratio:F2} < {Threshold:F2}; " +
                "document likely non-English. DocumentId={DocumentId}",
                ratio, MinEnglishCharRatio, documentId);
            return false;
        }

        // ── English function-word presence check ─────────────────────────────────
        // Convert to lower-case once and count matching function words.
        var lower = documentText.ToLowerInvariant();
        int hits   = 0;
        foreach (var word in EnglishFunctionWords)
        {
            if (lower.Contains(word, StringComparison.Ordinal)) hits++;
        }

        // Require at least 3 function words for a "likely English" determination.
        // Very domain-specific clinical text (e.g. ICD codes, lab values) may have fewer,
        // so the threshold is kept deliberately low.
        if (hits < 3)
        {
            _logger.LogWarning(
                "ClinicalExtractionLanguageGate: low English function-word count {Hits} < 3; " +
                "document may not be English. DocumentId={DocumentId}",
                hits, documentId);
            return false;
        }

        _logger.LogDebug(
            "ClinicalExtractionLanguageGate: document passed language gate. " +
            "DocumentId={DocumentId} AsciiRatio={Ratio:F2} FunctionWordHits={Hits}",
            documentId, ratio, hits);

        return true;
    }
}
