using System.Text;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.AI.DocumentParsing;

/// <summary>
/// Builds versioned prompt payloads for clinical document parsing (US_039 AC-2, AIR-002, AIR-O01).
///
/// Template files (document-parse-system.liquid, document-parse-schema.liquid) live alongside
/// this file and are loaded from the output directory at runtime.
///
/// Token budget enforcement (AIR-O01):
///   - Document content is capped at <see cref="MaxDocumentContentChars"/> (≈3000 tokens).
///   - System prompt is capped at <see cref="MaxSystemPromptChars"/> (≈1000 tokens).
///   - Total input budget is 4096 tokens; <c>max_tokens</c> is 1024 for output.
///
/// PII guard (AIR-S01):
///   - SSN-like patterns are stripped from document content before any model call.
///   - Document ID and category are included (opaque/non-PHI); patient name is never in the prompt.
/// </summary>
public sealed class DocumentParsingPromptBuilder
{
    // Token budget (AIR-O01): 4096 input ≈ 16384 chars; cap content to leave room for system prompt.
    internal const int MaxDocumentContentChars = 12_000;
    private const int MaxSystemPromptChars = 3_000;
    internal const int MaxOutputTokens = 1_024;

    private readonly ILogger<DocumentParsingPromptBuilder> _logger;

    // Lazy-loaded template (loaded once per service lifetime, thread-safe via double-checked lock).
    private string? _systemTemplate;
    private static readonly object TemplateLock = new();

    public DocumentParsingPromptBuilder(ILogger<DocumentParsingPromptBuilder> logger)
    {
        _logger = logger;
    }

    // ─── Tool definition (OpenAI function-calling) ────────────────────────────────

    /// <summary>
    /// Returns the JSON tool definition for <c>extract_document_data</c> used in the
    /// OpenAI chat completions request.
    /// </summary>
    public static string GetToolDefinitionJson()
    {
        // Inline the schema from document-parse-schema.liquid without file I/O overhead.
        return """
[
  {
    "type": "function",
    "function": {
      "name": "extract_document_data",
      "description": "Extracts structured clinical data from the provided document content.",
      "parameters": {
        "type": "object",
        "properties": {
          "extraction_possible": { "type": "boolean" },
          "confidence": { "type": "number" },
          "document_date": { "type": ["string", "null"] },
          "provider_name": { "type": ["string", "null"] },
          "summary": { "type": ["string", "null"] },
          "extracted_fields": {
            "type": "object",
            "additionalProperties": { "type": ["string", "null"] }
          },
          "manual_review_reason": { "type": ["string", "null"] }
        },
        "required": ["extraction_possible", "confidence", "extracted_fields"]
      }
    }
  }
]
""";
    }

    /// <summary>Returns the tool_choice JSON to force the model to call extract_document_data.</summary>
    public static string GetToolChoiceJson() =>
        """{"type": "function", "function": {"name": "extract_document_data"}}""";

    // ─── Prompt assembly ──────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the system and user messages for the OpenAI chat-completions request.
    /// </summary>
    public IReadOnlyList<(string Role, string Content)> BuildMessages(
        Guid             documentId,
        DocumentCategory category,
        string           contentType,
        string           documentText)
    {
        var systemPrompt  = BuildSystemPrompt(documentId, category, contentType);
        var userContent   = BuildUserContent(documentText);

        return new[]
        {
            ("system", systemPrompt),
            ("user",   userContent),
        };
    }

    // ─── Private helpers ──────────────────────────────────────────────────────────

    private string BuildSystemPrompt(Guid documentId, DocumentCategory category, string contentType)
    {
        var template = LoadSystemTemplate();
        var guidance = GetCategoryGuidance(category);

        var prompt = template
            .Replace("{{ document_id }}",       documentId.ToString())
            .Replace("{{ document_category }}", category.ToString())
            .Replace("{{ content_type }}",      contentType)
            .Replace("{{ timestamp }}",          DateTimeOffset.UtcNow.ToString("O"))
            .Replace("{{ max_output_tokens }}",  MaxOutputTokens.ToString())
            .Replace("{{ category_guidance }}",  guidance);

        // Cap system prompt to stay within token budget.
        if (prompt.Length > MaxSystemPromptChars)
        {
            prompt = prompt[..MaxSystemPromptChars] + "\n[system prompt truncated for token budget]";
            _logger.LogWarning(
                "DocumentParsingPromptBuilder: system prompt truncated for DocumentId={DocumentId}.",
                documentId);
        }

        return prompt;
    }

    private static string BuildUserContent(string documentText)
    {
        var sb = new StringBuilder();
        sb.AppendLine("## DOCUMENT CONTENT");
        sb.AppendLine("```");
        // Document text is already truncated and sanitised by the caller.
        sb.AppendLine(documentText);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("Extract all clinically significant data from this document using the extract_document_data function.");
        return sb.ToString();
    }

    private string LoadSystemTemplate()
    {
        if (_systemTemplate is not null) return _systemTemplate;

        lock (TemplateLock)
        {
            if (_systemTemplate is not null) return _systemTemplate;

            var basePath   = AppContext.BaseDirectory;
            var templatePath = Path.Combine(basePath, "AI", "DocumentParsing", "Prompts", "document-parse-system.liquid");

            if (File.Exists(templatePath))
            {
                _systemTemplate = File.ReadAllText(templatePath);
            }
            else
            {
                _logger.LogWarning(
                    "DocumentParsingPromptBuilder: template not found at {Path}; using inline default.",
                    templatePath);
                _systemTemplate = GetInlineSystemTemplate();
            }

            return _systemTemplate;
        }
    }

    private static string GetInlineSystemTemplate() =>
        """
You are a clinical document extraction specialist for UPACIP Medical Clinic.
Extract structured clinical data from the document content provided.
Document ID: {{ document_id }}. Category: {{ document_category }}. Type: {{ content_type }}.
{{ category_guidance }}
Invoke extract_document_data only. Max output tokens: {{ max_output_tokens }}.
Do NOT fabricate values. Set confidence based on text clarity (0.0–1.0). Include manual_review_reason if confidence < 0.70.
""";

    private static string GetCategoryGuidance(DocumentCategory category) => category switch
    {
        DocumentCategory.LabResult     =>
            "Focus on: test names, result values with units, reference ranges, specimen date, ordering physician, lab name, and any flagged abnormal values.",
        DocumentCategory.Prescription  =>
            "Focus on: medication name, dose, frequency, route, prescribing physician, prescription date, refills remaining, and patient instructions.",
        DocumentCategory.ClinicalNote  =>
            "Focus on: visit date, provider name, chief complaint, assessment, plan, diagnoses (ICD-10 if present), and follow-up instructions.",
        DocumentCategory.ImagingReport =>
            "Focus on: imaging modality, body part, date of study, radiologist/ordering physician, findings summary, and impression/conclusion.",
        _                              =>
            "Extract all clinically significant data present in the document.",
    };
}
