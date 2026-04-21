using UPACIP.Service.AI.ConversationalIntake;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Deterministic session lifecycle orchestration for AI conversational intake (US_027, FR-026).
///
/// Responsibilities:
///   - Start a new session or resume the most recent active session (EC-2).
///   - Process a single patient message by delegating to <see cref="IConversationalIntakeService"/>.
///   - Persist session state to Redis cache after every exchange (autosave, UXR-004).
///   - Generate the summary for AC-4 review.
///   - Complete the intake by persisting an <c>IntakeData</c> record.
///   - Transfer collected data to the manual form without data loss.
///
/// Contract:
///   - All methods returning <c>false</c> or an error variant must NOT throw.
///   - Session ownership is enforced: patientId must match the session's owner.
/// </summary>
public interface IAIIntakeSessionService
{
    /// <summary>
    /// Starts a new AI intake session or resumes the most recent active session for the
    /// specified patient (AC-1, EC-2).
    /// </summary>
    /// <param name="patientId">Authenticated patient's domain ID.</param>
    /// <param name="patientEmail">Patient email for logging correlation (not stored in state).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Start/resume response with greeting, history, and progress metadata.</returns>
    Task<StartAIIntakeResponse> StartOrResumeSessionAsync(
        Guid patientId,
        string patientEmail,
        CancellationToken ct = default);

    /// <summary>
    /// Processes a patient message for the given session.
    /// Invokes the AI orchestration, persists updated session state, and returns
    /// the next AI prompt with progress metadata (AC-2, AC-5).
    /// </summary>
    /// <param name="sessionId">Active session identifier.</param>
    /// <param name="patientId">Authenticated patient's domain ID (ownership check).</param>
    /// <param name="content">Patient's raw text reply (sanitised by the AI layer).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// Message response with next question, extracted value, and routing flags.
    /// Returns null when the session is not found or not owned by <paramref name="patientId"/>.
    /// </returns>
    Task<AIIntakeMessageResponse?> SendMessageAsync(
        Guid sessionId,
        Guid patientId,
        string content,
        CancellationToken ct = default);

    /// <summary>
    /// Generates the review summary for the specified session (AC-4).
    /// Returns null when the session is not found or mandatory fields are incomplete.
    /// </summary>
    Task<AIIntakeSummaryResponse?> GetSummaryAsync(
        Guid sessionId,
        Guid patientId,
        CancellationToken ct = default);

    /// <summary>
    /// Finalises the intake session by persisting an <c>IntakeData</c> record and
    /// marking the session as completed (AC-4).
    /// Returns null when the session is not found, not owned, or mandatory fields are incomplete.
    /// </summary>
    Task<CompleteIntakeResponse?> CompleteSessionAsync(
        Guid sessionId,
        Guid patientId,
        CancellationToken ct = default);

    /// <summary>
    /// Switches the session to manual mode, preserving all collected data for pre-filling
    /// the manual intake form without data loss (FL-004, US_028).
    /// </summary>
    Task<SwitchToManualResponse?> SwitchToManualAsync(
        Guid sessionId,
        Guid patientId,
        CancellationToken ct = default);

    /// <summary>
    /// Resumes (or starts) an AI intake session pre-populated with manually entered field values
    /// so the conversation continues from the first uncollected field (US_029, AC-2, AC-3).
    ///
    /// Merging strategy (EC-1):
    ///   Fields already in the session are overwritten only when the incoming manual value is
    ///   non-empty (most-recent-entry wins). The displaced value is captured in
    ///   <see cref="ResumeFromManualResult.Conflicts"/> for attribution (AC-4).
    ///
    /// Returns <c>null</c> when the AI service is unavailable (EC-2 — caller returns 503).
    /// </summary>
    Task<ResumeFromManualResult?> ResumeFromManualAsync(
        Guid patientId,
        IReadOnlyDictionary<string, string> manualFields,
        CancellationToken ct = default);
}

/// <summary>Result returned by <see cref="IAIIntakeSessionService.ResumeFromManualAsync"/>.</summary>
public sealed record ResumeFromManualResult
{
    public Guid SessionId { get; init; }
    /// <summary>The next field the AI should ask about. Null when all fields are complete.</summary>
    public string? NextField { get; init; }
    public IReadOnlyList<IntakeFieldConflict> Conflicts { get; init; } = [];
}
