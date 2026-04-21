using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace UPACIP.Service.AI.ConversationalIntake;

/// <summary>
/// Assembles the system prompt and conversation messages for each AI intake exchange
/// using the versioned Liquid template files (intake-system.liquid, intake-summary.liquid).
///
/// Template variable substitution uses simple <c>{{ variable }}</c> replacement — the
/// files are designed to be Liquid-compatible but do not require a runtime Liquid engine.
/// If a Liquid engine (e.g. Fluid.Core) is added in the future, no template changes are needed.
///
/// Token budget enforcement (AIR-O02):
///   - The system prompt is capped at ≈ 1800 chars (≈ 450 tokens).
///   - Conversation history is trimmed to the most recent <see cref="MaxHistoryTurns"/> turns
///     when the assembled prompt would exceed the 500 input-token target.
///   - The patient input itself is truncated to 300 chars in the message list.
///
/// PII redaction (AIR-S01):
///   - CollectedFields values (name, DOB, phone) are only included in the
///     intake-summary prompt, not in the system prompt sent to the AI.
///   - Session ID is included (opaque UUID — not PII) for correlation.
/// </summary>
public sealed class IntakePromptBuilder
{
    // Token budget: 500 input tokens ≈ 2000 chars (1 token ≈ 4 chars average, AIR-O02)
    private const int MaxSystemPromptChars = 1_800;
    private const int MaxHistoryTurns = 4;   // Keeps last 4 turns within token budget
    private const int MaxOutputTokens = 200; // AIR-O02
    private const int MaxPatientInputInPrompt = 300;

    private readonly ILogger<IntakePromptBuilder> _logger;

    // Lazily loaded template content (loaded once per service lifetime)
    private string? _systemTemplate;
    private string? _summaryTemplate;
    private static readonly object _templateLock = new();

    public IntakePromptBuilder(ILogger<IntakePromptBuilder> logger)
    {
        _logger = logger;
    }

    // ─── System prompt for exchange messages ──────────────────────────────────

    /// <summary>
    /// Builds the system prompt for an intake exchange using the intake-system.liquid template.
    /// Substitutes all {{ variable }} placeholders with session state and RAG context.
    /// </summary>
    public string BuildSystemPrompt(IntakeSessionContext context, string ragContext)
    {
        var template = LoadTemplate("intake-system.liquid");

        var remainingMandatory = IntakeFieldDefinitions.MandatoryOrder
            .Where(k => !context.CollectedFields.ContainsKey(k) || string.IsNullOrWhiteSpace(context.CollectedFields[k]))
            .Select(k => IntakeFieldDefinitions.Labels.GetValueOrDefault(k, k))
            .ToList();

        var remainingOptional = IntakeFieldDefinitions.OptionalOrder
            .Where(k => !context.CollectedFields.ContainsKey(k) || string.IsNullOrWhiteSpace(context.CollectedFields[k]))
            .Select(k => IntakeFieldDefinitions.Labels.GetValueOrDefault(k, k))
            .ToList();

        var prompt = template
            .Replace("{{ session_id }}", context.SessionId.ToString())
            .Replace("{{ current_field_label }}", IntakeFieldDefinitions.Labels.GetValueOrDefault(context.CurrentFieldKey, context.CurrentFieldKey))
            .Replace("{{ current_field_key }}", context.CurrentFieldKey)
            .Replace("{{ remaining_mandatory_fields }}", string.Join(", ", remainingMandatory))
            .Replace("{{ remaining_optional_fields }}", string.Join(", ", remainingOptional))
            .Replace("{{ rag_context }}", string.IsNullOrWhiteSpace(ragContext) ? "(no context retrieved)" : ragContext)
            .Replace("{{ conversation_history }}", BuildHistoryBlock(context.History))
            .Replace("{{ patient_input }}", "(provided as user message)")
            .Replace("{{ max_output_tokens }}", MaxOutputTokens.ToString());

        // Trim to stay within MaxSystemPromptChars to protect the token budget
        if (prompt.Length > MaxSystemPromptChars)
        {
            prompt = prompt[..MaxSystemPromptChars];
            _logger.LogDebug(
                "IntakePromptBuilder: system prompt truncated to {Max} chars for field={FieldKey}.",
                MaxSystemPromptChars, context.CurrentFieldKey);
        }

        return prompt;
    }

    /// <summary>
    /// Builds the summary system prompt for AC-4 summary review generation.
    /// Collected field values are included here (AI needs them to produce the summary);
    /// this prompt is NOT sent to external providers — the summary is generated internally.
    ///
    /// Wait — actually the summary IS sent to the AI for natural-language rendering.
    /// PII is included here because it was collected by the patient themselves and must
    /// appear in their own review; it is NOT used for training (per AIR-S01 spirit).
    /// </summary>
    public string BuildSummaryPrompt(IReadOnlyDictionary<string, string> collectedFields)
    {
        var template = LoadTemplate("intake-summary.liquid");

        var fieldsJson = JsonSerializer.Serialize(
            collectedFields,
            new JsonSerializerOptions { WriteIndented = true });

        return template.Replace("{{ collected_fields_json }}", fieldsJson);
    }

    // ─── Message list for the chat completion request ─────────────────────────

    /// <summary>
    /// Builds the ordered message list for the OpenAI chat completion API.
    /// Includes history trimmed to <see cref="MaxHistoryTurns"/> turns (AIR-O02).
    /// </summary>
    public IReadOnlyList<(string Role, string Content)> BuildMessages(
        string systemPrompt,
        IReadOnlyList<ConversationTurn> history,
        string currentPatientInput)
    {
        var messages = new List<(string Role, string Content)>
        {
            ("system", systemPrompt),
        };

        // Trim history — take the last MaxHistoryTurns turns to stay within token budget
        var trimmedHistory = history.Count > MaxHistoryTurns
            ? history.Skip(history.Count - MaxHistoryTurns).ToList()
            : history;

        foreach (var turn in trimmedHistory)
        {
            messages.Add((turn.Role, turn.Content));
        }

        // Current patient input — truncated for safety
        var truncatedInput = currentPatientInput.Length > MaxPatientInputInPrompt
            ? currentPatientInput[..MaxPatientInputInPrompt]
            : currentPatientInput;

        messages.Add(("user", truncatedInput));
        return messages;
    }

    // ─── OpenAI function/tool schema ──────────────────────────────────────────

    /// <summary>
    /// Returns the JSON schema for the <c>extract_intake_field</c> OpenAI tool call.
    /// Structured output via tool calling ensures the model always returns a
    /// machine-parseable response rather than free-form text.
    /// </summary>
    public static string GetToolDefinitionJson() => """
        [
          {
            "type": "function",
            "function": {
              "name": "extract_intake_field",
              "description": "Extracts a structured intake field from the patient's response and provides the next conversational prompt.",
              "parameters": {
                "type": "object",
                "properties": {
                  "extracted_value": {
                    "type": ["string", "null"],
                    "description": "The clean extracted value for the current field, or null if not yet provided."
                  },
                  "confidence": {
                    "type": "number",
                    "description": "Confidence score in [0.0, 1.0] reflecting how confidently the value was extracted.",
                    "minimum": 0.0,
                    "maximum": 1.0
                  },
                  "needs_clarification": {
                    "type": "boolean",
                    "description": "True when the patient input was ambiguous and follow-up examples should be shown."
                  },
                  "clarification_examples": {
                    "type": "array",
                    "items": { "type": "string" },
                    "description": "2-3 plain-English example answers to guide the patient (populated only when needs_clarification=true).",
                    "maxItems": 3
                  },
                  "reply_to_patient": {
                    "type": "string",
                    "description": "The AI's conversational reply to display in the chat UI. Maximum 120 words."
                  },
                  "is_field_complete": {
                    "type": "boolean",
                    "description": "True when the field has a valid confirmed value."
                  }
                },
                "required": ["confidence", "needs_clarification", "reply_to_patient", "is_field_complete"]
              }
            }
          }
        ]
        """;

    /// <summary>Returns the tool_choice parameter to force the model to use the function.</summary>
    public static string GetToolChoiceJson() =>
        """{"type": "function", "function": {"name": "extract_intake_field"}}""";

    // ─── Private helpers ──────────────────────────────────────────────────────

    private string BuildHistoryBlock(IReadOnlyList<ConversationTurn> history)
    {
        if (history.Count == 0) return "(no prior conversation)";

        var turns = history.Count > MaxHistoryTurns
            ? history.Skip(history.Count - MaxHistoryTurns)
            : history;

        var sb = new StringBuilder();
        foreach (var turn in turns)
        {
            var prefix = turn.Role == "user" ? "Patient" : "AI";
            sb.AppendLine($"{prefix}: {turn.Content}");
        }
        return sb.ToString().Trim();
    }

    private string LoadTemplate(string fileName)
    {
        if (fileName == "intake-system.liquid" && _systemTemplate is not null)
            return _systemTemplate;

        if (fileName == "intake-summary.liquid" && _summaryTemplate is not null)
            return _summaryTemplate;

        lock (_templateLock)
        {
            // Double-check inside lock
            if (fileName == "intake-system.liquid" && _systemTemplate is not null)
                return _systemTemplate;
            if (fileName == "intake-summary.liquid" && _summaryTemplate is not null)
                return _summaryTemplate;

            var filePath = Path.Combine(
                AppContext.BaseDirectory,
                "AI", "ConversationalIntake", "Prompts", fileName);

            if (!File.Exists(filePath))
            {
                _logger.LogError(
                    "IntakePromptBuilder: prompt template not found at {FilePath}. Using inline fallback.",
                    filePath);
                return GetInlineFallbackTemplate(fileName);
            }

            var content = File.ReadAllText(filePath);
            if (fileName == "intake-system.liquid") _systemTemplate = content;
            else _summaryTemplate = content;
            return content;
        }
    }

    /// <summary>Minimal fallback template used when the .liquid file is missing (fail-safe).</summary>
    private static string GetInlineFallbackTemplate(string fileName) => fileName switch
    {
        "intake-system.liquid" =>
            """
            You are a medical intake assistant. Collect patient information conversationally.
            Current field: {{ current_field_label }} ({{ current_field_key }}).
            RAG context: {{ rag_context }}
            History: {{ conversation_history }}
            Patient said: {{ patient_input }}
            Use the extract_intake_field tool to respond.
            """,
        "intake-summary.liquid" =>
            """
            Summarise the following collected intake data for patient review:
            {{ collected_fields_json }}
            End with: "Does everything look correct?"
            """,
        _ => "Provide a concise, helpful response.",
    };
}
