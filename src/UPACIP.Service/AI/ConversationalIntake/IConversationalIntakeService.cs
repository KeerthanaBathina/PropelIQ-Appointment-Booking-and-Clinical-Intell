namespace UPACIP.Service.AI.ConversationalIntake;

/// <summary>
/// Core orchestration interface for the AI conversational intake workflow (AIR-001, FR-026).
///
/// Contract:
///   - Never throws an exception to the caller; all AI provider failures are
///     handled internally and surfaced via <see cref="IntakeExchangeResult.ShouldSwitchToManual"/>.
///   - Logs are PII-free: patient name, DOB, phone, and email are never written to
///     log sinks (AIR-S01).
///   - All inputs are sanitised for prompt injection before reaching any model (AIR-S06).
/// </summary>
public interface IConversationalIntakeService
{
    /// <summary>
    /// Processes a single patient message in an intake session.
    ///
    /// Orchestration order (models.md UC-002):
    ///   1. Sanitise patient input (AIR-S06).
    ///   2. Retrieve RAG context from pgvector (AIR-R02).
    ///   3. Build grounded prompt (IntakePromptBuilder, AIR-O02 token budget).
    ///   4. Call OpenAI GPT-4o-mini primary via Polly circuit breaker (AIR-O04).
    ///   5. If circuit open or confidence &lt; 80%: call Anthropic Claude 3.5 fallback (AIR-010).
    ///   6. If both fail consecutively ≥ FallbackThreshold: return manual-form handoff (NFR-022).
    ///   7. Validate and return extracted field + next question (AC-2).
    /// </summary>
    /// <param name="sessionContext">Full session state for this exchange.</param>
    /// <param name="patientInput">Raw patient utterance (will be sanitised by this method).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Exchange result with next question, extracted value, and routing flags.</returns>
    Task<IntakeExchangeResult> ProcessMessageAsync(
        IntakeSessionContext sessionContext,
        string patientInput,
        CancellationToken ct = default);

    /// <summary>
    /// Generates the natural-language summary of all collected fields for AC-4 review.
    /// Returned summary text is suitable for direct display to the patient.
    /// </summary>
    /// <param name="sessionId">Session identifier (for logging correlation).</param>
    /// <param name="collectedFields">All collected field values for this session.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Summary result with formatted text and structured field list.</returns>
    Task<IntakeSummaryResult> GenerateSummaryAsync(
        Guid sessionId,
        IReadOnlyDictionary<string, string> collectedFields,
        CancellationToken ct = default);
}
