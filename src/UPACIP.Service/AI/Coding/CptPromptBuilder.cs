using System.Text.Json;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess.Entities;

namespace UPACIP.Service.AI.Coding;

/// <summary>
/// Composes the system and user prompts for the CPT procedure code mapping AI pipeline
/// by loading versioned Liquid templates from disk and performing variable substitution
/// (US_048, AIR-003, AC-1, AC-2, AC-3).
///
/// Templates are stored alongside the service assembly in:
///   <c>AI/Coding/Prompts/cpt-coding-system.v1.0.liquid</c>
///   <c>AI/Coding/Prompts/cpt-coding-user.v1.0.liquid</c>
///
/// The substitution engine is a simple key-value replace; no Liquid engine dependency
/// is required, matching the pattern used by <see cref="Icd10PromptBuilder"/>.
///
/// Token budget enforcement (AIR-O03):
///   Combined prompt is targeted to ≤ 6 000 characters (~2 000 tokens at 3 chars/token).
/// </summary>
public sealed class CptPromptBuilder
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants (must match cpt-coding-guardrails.json §TokenBudget)
    // ─────────────────────────────────────────────────────────────────────────

    private const int MaxProcedureChars = 5_000;
    private const int MaxInputChars     = 6_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented        = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Dependencies
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ILogger<CptPromptBuilder> _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public CptPromptBuilder(ILogger<CptPromptBuilder> logger)
    {
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the (system, user) message tuple for the CPT coding tool call.
    /// </summary>
    /// <param name="procedureDescriptions">
    /// Map of <c>ExtractedData</c> ID → sanitised procedure text.
    /// </param>
    /// <param name="ragContext">
    /// CPT coding guideline chunks retrieved from pgvector (may be empty string).
    /// </param>
    /// <param name="bundleRulesContext">
    /// Active CPT bundle rules summary for injection into system prompt (may be empty string).
    /// </param>
    /// <param name="correlationId">Correlation ID stamped into the prompt for traceability.</param>
    public (string SystemPrompt, string UserPrompt) Build(
        IReadOnlyDictionary<Guid, string> procedureDescriptions,
        string                            ragContext,
        string                            bundleRulesContext,
        Guid                              correlationId)
    {
        var analysisId  = correlationId;
        var patientMask = $"***{correlationId.ToString()[^4..]}"; // last 4 chars only (AIR-S01)
        var timestamp   = DateTime.UtcNow.ToString("o");

        // Serialise procedures to JSON for injection into the prompt.
        var procArray = procedureDescriptions.Select(kvp => new
        {
            procedure_id   = kvp.Key.ToString(),
            procedure_text = kvp.Value,
        }).ToList();

        var procJson = JsonSerializer.Serialize(procArray, JsonOptions);
        if (procJson.Length > MaxProcedureChars)
            procJson = procJson[..MaxProcedureChars];

        var systemTemplate = LoadTemplate("cpt-coding-system.v1.0.liquid");
        var userTemplate   = LoadTemplate("cpt-coding-user.v1.0.liquid");

        var systemPrompt = systemTemplate
            .Replace("{{ analysis_id }}",          analysisId.ToString())
            .Replace("{{ patient_id_masked }}",    patientMask)
            .Replace("{{ timestamp }}",            timestamp)
            .Replace("{{ procedure_count }}",      procedureDescriptions.Count.ToString())
            .Replace("{{ rag_context }}",          ragContext)
            .Replace("{{ bundle_rules_context }}", bundleRulesContext)
            .Replace("{{ procedures_json }}",      procJson);

        var userPrompt = userTemplate
            .Replace("{{ analysis_id }}",     analysisId.ToString())
            .Replace("{{ procedures_json }}", procJson);

        var combinedLen = systemPrompt.Length + userPrompt.Length;
        if (combinedLen > MaxInputChars)
        {
            _logger.LogWarning(
                "CptPromptBuilder: combined prompt length {Len} exceeds budget {Budget}; " +
                "procedures_json may be truncated. CorrelationId={CorrelationId}",
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
            "CptPromptBuilder: template '{File}' not found at {Path}; using inline default.", fileName, path);

        return fileName.Contains("system")
            ? BuildInlineSystemTemplate()
            : BuildInlineUserTemplate();
    }

    private static string BuildInlineSystemTemplate() => """
        You are a certified medical coding specialist for UPACIP Medical Clinic.
        Analysis ID: {{ analysis_id }}. Timestamp: {{ timestamp }}.
        Map each procedure in {{ procedures_json }} to the most appropriate CPT code.
        Return structured JSON by calling map_cpt_codes exactly once.
        For uncodable procedures, use code_value: "UNCODABLE" with confidence: 0.00.
        """;

    private static string BuildInlineUserTemplate() => """
        Map these procedures to CPT codes. Analysis ID: {{ analysis_id }}.
        {{ procedures_json }}
        Call map_cpt_codes now.
        """;
}
