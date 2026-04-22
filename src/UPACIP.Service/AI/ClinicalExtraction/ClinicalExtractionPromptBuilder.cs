using System.Text;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.AI.ClinicalExtraction;

/// <summary>
/// Builds versioned prompt payloads for clinical data extraction (US_040 AC-1–AC-5, AIR-002, AIR-O01).
///
/// Token budget (AIR-O01):
///   - Document content is capped at <see cref="MaxDocumentContentChars"/> (≈3000 tokens).
///   - System prompt is capped at <see cref="MaxSystemPromptChars"/> (≈750 tokens).
///   - Total input budget: 4096 tokens; output budget: 2048 tokens.
///
/// Template loading:
///   - <c>clinical-extraction-system.liquid</c> loaded from output directory once and cached.
///   - Falls back to an inline default when the file is absent (dev-time safety net).
/// </summary>
public sealed class ClinicalExtractionPromptBuilder
{
    // Token budget constants (match guardrails.json).
    internal const int MaxDocumentContentChars = 12_000;
    private  const int MaxSystemPromptChars    = 3_000;
    internal const int MaxOutputTokens         = 2_048;

    private readonly ILogger<ClinicalExtractionPromptBuilder> _logger;

    private string? _systemTemplate;
    private static readonly object TemplateLock = new();

    public ClinicalExtractionPromptBuilder(ILogger<ClinicalExtractionPromptBuilder> logger)
    {
        _logger = logger;
    }

    // ─── Tool definition (OpenAI function-calling) ────────────────────────────────

    /// <summary>Returns the JSON tools array for the <c>extract_clinical_data</c> function.</summary>
    public static string GetToolDefinitionJson() => """
[
  {
    "type": "function",
    "function": {
      "name": "extract_clinical_data",
      "description": "Extracts structured clinical entities (medications, diagnoses, procedures, allergies) from document content with per-item confidence scores and source attribution.",
      "parameters": {
        "type": "object",
        "properties": {
          "extraction_outcome": {
            "type": "string",
            "enum": ["extracted", "no-data-extracted", "unsupported-language"]
          },
          "confidence": { "type": "number", "description": "Overall extraction quality score 0.0–1.0." },
          "medications": {
            "type": "array",
            "items": {
              "type": "object",
              "properties": {
                "drug_name":             { "type": "string" },
                "dosage":                { "type": ["string","null"] },
                "frequency":             { "type": ["string","null"] },
                "route":                 { "type": ["string","null"] },
                "prescribing_physician": { "type": ["string","null"] },
                "prescription_date":     { "type": ["string","null"] },
                "raw_text":              { "type": ["string","null"] },
                "page_number":           { "type": "integer" },
                "extraction_region":     { "type": "string" },
                "confidence":            { "type": ["number","null"], "description": "Per-item confidence 0.0–1.0; null if you cannot assign a score." }
              },
              "required": ["drug_name","page_number","extraction_region","confidence"]
            }
          },
          "diagnoses": {
            "type": "array",
            "items": {
              "type": "object",
              "properties": {
                "condition_name":    { "type": "string" },
                "icd_code":          { "type": ["string","null"] },
                "diagnosis_date":    { "type": ["string","null"] },
                "treating_provider": { "type": ["string","null"] },
                "status":            { "type": ["string","null"] },
                "raw_text":          { "type": ["string","null"] },
                "page_number":       { "type": "integer" },
                "extraction_region": { "type": "string" },
                "confidence":        { "type": ["number","null"], "description": "Per-item confidence 0.0–1.0; null if you cannot assign a score." }
              },
              "required": ["condition_name","page_number","extraction_region","confidence"]
            }
          },
          "procedures": {
            "type": "array",
            "items": {
              "type": "object",
              "properties": {
                "procedure_name":       { "type": "string" },
                "procedure_date":       { "type": ["string","null"] },
                "performing_physician": { "type": ["string","null"] },
                "facility":             { "type": ["string","null"] },
                "cpt_code":             { "type": ["string","null"] },
                "raw_text":             { "type": ["string","null"] },
                "page_number":          { "type": "integer" },
                "extraction_region":    { "type": "string" },
                "confidence":           { "type": ["number","null"], "description": "Per-item confidence 0.0–1.0; null if you cannot assign a score." }
              },
              "required": ["procedure_name","page_number","extraction_region","confidence"]
            }
          },
          "allergies": {
            "type": "array",
            "items": {
              "type": "object",
              "properties": {
                "allergen":          { "type": "string" },
                "reaction_type":     { "type": ["string","null"] },
                "severity":          { "type": ["string","null"] },
                "onset_date":        { "type": ["string","null"] },
                "status":            { "type": ["string","null"] },
                "raw_text":          { "type": ["string","null"] },
                "page_number":       { "type": "integer" },
                "extraction_region": { "type": "string" },
                "confidence":        { "type": ["number","null"], "description": "Per-item confidence 0.0–1.0; null if you cannot assign a score." }
              },
              "required": ["allergen","page_number","extraction_region","confidence"]
            }
          },
          "outcome_reason": { "type": ["string","null"] }
        },
        "required": ["extraction_outcome","confidence"]
      }
    }
  }
]
""";

    /// <summary>Returns the tool_choice JSON forcing extract_clinical_data invocation.</summary>
    public static string GetToolChoiceJson() =>
        """{"type": "function", "function": {"name": "extract_clinical_data"}}""";

    // ─── Prompt assembly ──────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the system + user message list for the OpenAI chat-completions request.
    /// Document content MUST already be sanitised (SSN-redacted, injection-blocked).
    /// </summary>
    public IReadOnlyList<(string Role, string Content)> BuildMessages(
        Guid             documentId,
        DocumentCategory category,
        string           sanitisedDocumentText)
    {
        var systemPrompt = BuildSystemPrompt(documentId, category);
        var userContent  = BuildUserContent(sanitisedDocumentText);

        return new[]
        {
            ("system", systemPrompt),
            ("user",   userContent),
        };
    }

    // ─── Private helpers ──────────────────────────────────────────────────────────

    private string BuildSystemPrompt(Guid documentId, DocumentCategory category)
    {
        var template = LoadSystemTemplate();

        var prompt = template
            .Replace("{{ document_id }}",       documentId.ToString())
            .Replace("{{ document_category }}", category.ToString())
            .Replace("{{ timestamp }}",          DateTimeOffset.UtcNow.ToString("O"))
            .Replace("{{ max_output_tokens }}",  MaxOutputTokens.ToString());

        if (prompt.Length > MaxSystemPromptChars)
        {
            prompt = prompt[..MaxSystemPromptChars] + "\n[system prompt truncated for token budget]";
            _logger.LogWarning(
                "ClinicalExtractionPromptBuilder: system prompt truncated. DocumentId={DocumentId}",
                documentId);
        }

        return prompt;
    }

    private static string BuildUserContent(string documentText)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## DOCUMENT CONTENT");
        sb.AppendLine("```");

        // Content already truncated and sanitised by ClinicalExtractionService.
        sb.AppendLine(documentText);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("Extract all clinical entities from this document using the extract_clinical_data function.");
        return sb.ToString();
    }

    private string LoadSystemTemplate()
    {
        if (_systemTemplate is not null) return _systemTemplate;

        lock (TemplateLock)
        {
            if (_systemTemplate is not null) return _systemTemplate;

            var path = Path.Combine(AppContext.BaseDirectory, "AI", "ClinicalExtraction",
                "Prompts", "clinical-extraction-system.liquid");

            if (File.Exists(path))
            {
                _systemTemplate = File.ReadAllText(path);
            }
            else
            {
                _logger.LogWarning(
                    "ClinicalExtractionPromptBuilder: template not found at {Path}; using inline default.",
                    path);
                _systemTemplate = GetInlineSystemTemplate();
            }

            return _systemTemplate;
        }
    }

    private static string GetInlineSystemTemplate() =>
        """
You are a clinical data extraction specialist for UPACIP Medical Clinic.
Document ID: {{ document_id }}. Category: {{ document_category }}. Time: {{ timestamp }}.
Extract medications (drug_name, dosage, frequency, prescribing_physician),
diagnoses (condition_name, icd_code, treating_provider, status),
procedures (procedure_name, performing_physician, cpt_code),
and allergies (allergen, reaction_type, severity).
Every item MUST include page_number (int), extraction_region (string), and confidence (number 0.0–1.0).
Set confidence=null ONLY when you genuinely cannot assess extraction quality for that item.
Items with confidence below 0.80 or null will be flagged for mandatory staff review.
Set extraction_outcome="no-data-extracted" if nothing found.
Set extraction_outcome="unsupported-language" if document is not English.
Call extract_clinical_data once. Max tokens: {{ max_output_tokens }}.
""";
}
