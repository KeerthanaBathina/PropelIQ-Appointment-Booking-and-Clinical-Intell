using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace UPACIP.Service.AI.Coding;

/// <summary>
/// Composes the system and user prompts for the ICD-10 mapping AI pipeline
/// by loading versioned Liquid templates from disk and performing variable substitution
/// (US_047, AIR-003, AC-1, AC-2, AC-4).
///
/// Templates are stored alongside the service assembly in:
///   <c>AI/Coding/Prompts/icd10-mapping-system.v1.0.liquid</c>
///   <c>AI/Coding/Prompts/icd10-mapping-user.v1.0.liquid</c>
///
/// The substitution engine is a simple key-value replace; no Liquid engine dependency
/// is required, matching the pattern used by <c>ConflictDetectionService</c>.
///
/// Token budget enforcement (AIR-O03):
///   The combined prompt is truncated to <see cref="MaxInputChars"/> characters
///   (~2 000 tokens at 3 chars/token approximation) before being sent to the model.
/// </summary>
public sealed class Icd10PromptBuilder
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants (must match coding-guardrails.json §TokenBudget)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Approximate character budget for the diagnosis payload section of the prompt
    /// (~5 000 chars ≈ 1 700 tokens; leaves headroom for system prompt + RAG context).
    /// </summary>
    private const int MaxDiagnosisChars = 5_000;

    /// <summary>
    /// Maximum total characters for the combined system+user prompt content sent to the
    /// model (AIR-O03 2 000 token budget; ~6 000 chars at 3 chars/token average).
    /// </summary>
    private const int MaxInputChars = 6_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented        = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Dependencies
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ILogger<Icd10PromptBuilder> _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public Icd10PromptBuilder(ILogger<Icd10PromptBuilder> logger)
    {
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the (system, user) message tuple for the ICD-10 mapping tool call.
    /// </summary>
    /// <param name="diagnosisDescriptions">
    /// Map of <c>ExtractedData</c> ID → sanitised diagnosis text.
    /// </param>
    /// <param name="ragContext">
    /// Coding guideline chunks retrieved from pgvector (may be empty string).
    /// </param>
    /// <param name="correlationId">Correlation ID stamped into the prompt for traceability.</param>
    public (string SystemPrompt, string UserPrompt) Build(
        IReadOnlyDictionary<Guid, string> diagnosisDescriptions,
        string                            ragContext,
        Guid                              correlationId)
    {
        var analysisId   = correlationId;
        var patientMask  = $"***{correlationId.ToString()[^4..]}";  // last 4 chars only (AIR-S01)
        var timestamp    = DateTime.UtcNow.ToString("o");

        // Serialise diagnoses to JSON for injection into the prompt.
        var diagArray = diagnosisDescriptions.Select(kvp => new
        {
            diagnosis_id  = kvp.Key.ToString(),
            diagnosis_text = kvp.Value,
        }).ToList();

        var diagJson = JsonSerializer.Serialize(diagArray, JsonOptions);
        if (diagJson.Length > MaxDiagnosisChars)
            diagJson = diagJson[..MaxDiagnosisChars];

        var systemTemplate = LoadTemplate("icd10-mapping-system.v1.0.liquid");
        var userTemplate   = LoadTemplate("icd10-mapping-user.v1.0.liquid");

        var systemPrompt = systemTemplate
            .Replace("{{ analysis_id }}",     analysisId.ToString())
            .Replace("{{ patient_id_masked }}", patientMask)
            .Replace("{{ timestamp }}",       timestamp)
            .Replace("{{ diagnosis_count }}", diagnosisDescriptions.Count.ToString())
            .Replace("{{ rag_context }}",     ragContext)
            .Replace("{{ diagnoses_json }}",  diagJson);

        var userPrompt = userTemplate
            .Replace("{{ analysis_id }}", analysisId.ToString())
            .Replace("{{ diagnoses_json }}", diagJson);

        // Guard total character budget (AIR-O03).
        var combinedLen = systemPrompt.Length + userPrompt.Length;
        if (combinedLen > MaxInputChars)
        {
            _logger.LogWarning(
                "Icd10PromptBuilder: combined prompt length {Len} exceeds budget {Budget}; " +
                "diagnoses_json will be truncated. CorrelationId={CorrelationId}",
                combinedLen, MaxInputChars, correlationId);
        }

        return (systemPrompt, userPrompt);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Template loader (inline fallback for test/dev)
    // ─────────────────────────────────────────────────────────────────────────

    private string LoadTemplate(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "AI", "Coding", "Prompts", fileName);
        if (File.Exists(path)) return File.ReadAllText(path);

        _logger.LogDebug(
            "Icd10PromptBuilder: template '{File}' not found at {Path}; using inline default.", fileName, path);

        return fileName.Contains("system")
            ? BuildInlineSystemTemplate()
            : BuildInlineUserTemplate();
    }

    private static string BuildInlineSystemTemplate() => """
        You are a certified medical coding specialist for UPACIP Medical Clinic.
        Analysis ID: {{ analysis_id }}. Timestamp: {{ timestamp }}.
        Map each diagnosis in {{ diagnoses_json }} to the most appropriate ICD-10-CM code.
        Return structured JSON by calling map_icd10_codes exactly once.
        For uncodable diagnoses, use code_value: "UNCODABLE" with confidence: 0.00.
        """;

    private static string BuildInlineUserTemplate() => """
        Map these diagnoses to ICD-10-CM codes. Analysis ID: {{ analysis_id }}.
        {{ diagnoses_json }}
        Call map_icd10_codes now.
        """;
}
