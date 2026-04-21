namespace UPACIP.Service.AI.ConversationalIntake;

/// <summary>
/// Snapshot of a conversational intake session passed from the API layer to
/// <see cref="IConversationalIntakeService"/>. Contains everything the orchestration
/// needs to build the grounded prompt and advance the conversation state.
///
/// Note: PII fields (name, DOB, contact details) live in <see cref="CollectedFields"/>
/// and must NOT appear in log messages (AIR-S01 — redact PII before external API calls
/// is handled in <see cref="IntakePromptBuilder"/>).
/// </summary>
public sealed class IntakeSessionContext
{
    /// <summary>Stable session identifier (opaque to the AI layer).</summary>
    public required Guid SessionId { get; init; }

    /// <summary>Patient identifier — used for RAG personalisation lookups (no PII logged).</summary>
    public required Guid PatientId { get; init; }

    /// <summary>
    /// The field key that the AI is currently trying to collect.
    /// Resolved by the API layer using <see cref="IntakeFieldDefinitions.NextFieldToCollect"/>.
    /// </summary>
    public required string CurrentFieldKey { get; init; }

    /// <summary>
    /// Fields already collected this session.
    /// Key = field key constant from <see cref="IntakeFieldDefinitions"/>.
    /// Value = patient-provided value (verbatim, validated at intake time).
    /// </summary>
    public IReadOnlyDictionary<string, string> CollectedFields { get; init; }
        = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Ordered conversation history for this session (most recent last).
    /// Trimmed by <see cref="IntakePromptBuilder"/> to respect the token budget (AIR-O02).
    /// </summary>
    public IReadOnlyList<ConversationTurn> History { get; init; } = [];

    /// <summary>Total number of AI–patient exchanges so far (used to enforce MaxTurnsPerSession).</summary>
    public int TurnCount { get; init; }

    /// <summary>Number of consecutive AI provider failures (used for manual-form fallback gate).</summary>
    public int ConsecutiveProviderFailures { get; init; }
}

/// <summary>A single exchange turn in the conversation transcript.</summary>
public sealed class ConversationTurn
{
    /// <summary>"user" | "assistant"</summary>
    public required string Role { get; init; }

    /// <summary>Message content (patient reply or AI question).</summary>
    public required string Content { get; init; }

    /// <summary>UTC timestamp of the turn for ordering and resume diagnostics (EC-2).</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Result of processing a single patient message (AC-2).</summary>
public sealed class IntakeExchangeResult
{
    /// <summary>The AI reply text to display in the chat UI.</summary>
    public required string ReplyToPatient { get; init; }

    /// <summary>
    /// The field key that was being collected in this exchange.
    /// </summary>
    public required string FieldKey { get; init; }

    /// <summary>
    /// Extracted value for <see cref="FieldKey"/>; null when the patient did not yet provide one.
    /// </summary>
    public string? ExtractedValue { get; init; }

    /// <summary>True when <see cref="ExtractedValue"/> is valid and the field is complete.</summary>
    public bool IsFieldComplete { get; init; }

    /// <summary>
    /// True when the patient input was ambiguous and clarification examples are provided (EC-1).
    /// </summary>
    public bool NeedsClarification { get; init; }

    /// <summary>Plain-English examples for the patient when <see cref="NeedsClarification"/> is true (EC-1).</summary>
    public IReadOnlyList<string> ClarificationExamples { get; init; } = [];

    /// <summary>True when all mandatory fields are now complete and the summary is ready for review (AC-4).</summary>
    public bool IsSummaryReady { get; init; }

    /// <summary>
    /// True when the AI cannot continue (circuit open + fallback exhausted).
    /// The UI should surface the switch-to-manual CTA.
    /// </summary>
    public bool ShouldSwitchToManual { get; init; }

    /// <summary>
    /// The next field key to collect after this exchange, or null when all fields are complete.
    /// </summary>
    public string? NextFieldKey { get; init; }

    /// <summary>Identifies which AI provider generated this response ("openai", "anthropic", "fallback").</summary>
    public string Provider { get; init; } = "fallback";

    /// <summary>AI confidence score in [0, 1]. Values below the threshold trigger clarification (EC-1).</summary>
    public double Confidence { get; init; }
}

/// <summary>Result of generating the summary for review before submission (AC-4).</summary>
public sealed class IntakeSummaryResult
{
    /// <summary>The AI-generated summary text for review display.</summary>
    public required string SummaryText { get; init; }

    /// <summary>
    /// Collected fields serialised as label → value for table rendering in the UI.
    /// </summary>
    public IReadOnlyList<IntakeSummaryField> Fields { get; init; } = [];

    /// <summary>Number of mandatory fields collected (AC-5).</summary>
    public int MandatoryCollectedCount { get; init; }

    /// <summary>Total mandatory fields required (AC-5).</summary>
    public int MandatoryTotalCount { get; init; }
}

/// <summary>A single field entry in the summary display.</summary>
public sealed class IntakeSummaryField
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string Value { get; init; }
    public bool IsMandatory { get; init; }
    public bool IsEditable { get; init; } = true;
}
