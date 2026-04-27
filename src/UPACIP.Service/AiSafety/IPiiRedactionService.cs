namespace UPACIP.Service.AiSafety;

/// <summary>
/// Service for detecting, redacting, and re-associating PII in outbound AI prompts
/// and inbound AI responses (US_074 task_001, AC-3, AC-4, AIR-S01).
///
/// <para>Responsibilities:</para>
/// <list type="bullet">
///   <item>Detect and redact six PII categories (name, DOB, SSN, phone, email, address)
///   from prompt text before transmission to external AI providers (AC-3).</item>
///   <item>Replace PII with deterministic placeholder tokens (<c>[CATEGORY_N]</c>) and
///   maintain a per-request mapping in <see cref="PiiRedactionContext"/> (AC-4).</item>
///   <item>Apply the <see cref="MedicalTermAllowlist"/> to prevent false-positive redaction
///   of medical eponyms that resemble personal names (edge case).</item>
///   <item>Validate that no residual PII remains after redaction (<see cref="ContainsPii"/>).</item>
///   <item>Restore original PII values in responses when needed for internal use
///   (<see cref="RestorePii"/>); note that AI provider responses should use
///   <see cref="PiiRedactionContext.PatientId"/> for re-association per AC-4 and
///   restored PII must NEVER be stored in AI result tables or sent to external systems.</item>
/// </list>
///
/// <para>Implementation is stateless — registered as Singleton in the DI container.</para>
/// </summary>
public interface IPiiRedactionService
{
    /// <summary>
    /// Scans <paramref name="inputText"/> for all six PII categories, replaces matches
    /// with placeholder tokens, and returns a populated <see cref="PiiRedactionContext"/>.
    ///
    /// <para>Detection order (most specific first to avoid false positives):</para>
    /// <list type="number">
    ///   <item>SSN — most specific (9-digit structured pattern).</item>
    ///   <item>Email.</item>
    ///   <item>Phone.</item>
    ///   <item>DOB.</item>
    ///   <item>Address.</item>
    ///   <item>Name — least specific; skips terms in <see cref="MedicalTermAllowlist"/>.</item>
    /// </list>
    /// </summary>
    /// <param name="inputText">Prompt text to scan and redact.</param>
    /// <param name="patientId">Internal patient identifier for the context (AC-4).</param>
    /// <param name="patientName">
    /// Optional patient full name for exact and token-match name redaction.
    /// When null, name-pattern redaction is skipped.
    /// </param>
    /// <param name="dateOfBirth">Optional DOB for exact date-match redaction.</param>
    /// <param name="phoneNumber">Optional phone for exact phone-match redaction.</param>
    /// <returns>Tuple of the redacted text and the populated <see cref="PiiRedactionContext"/>.</returns>
    (string redactedText, PiiRedactionContext context) RedactPii(
        string  inputText,
        Guid    patientId,
        string? patientName  = null,
        string? dateOfBirth  = null,
        string? phoneNumber  = null);

    /// <summary>
    /// Replaces placeholder tokens in <paramref name="aiResponseText"/> with their
    /// original PII values stored in <paramref name="context"/>.
    ///
    /// <para>
    /// <b>IMPORTANT</b>: This method is intended for internal display only.
    /// Per AC-4, AI provider responses are stored and associated with patients via
    /// <see cref="PiiRedactionContext.PatientId"/> — restored PII must never be persisted
    /// in AI result tables or transmitted to external systems.
    /// </para>
    /// </summary>
    /// <param name="aiResponseText">Response text containing placeholder tokens.</param>
    /// <param name="context">Context carrying the token-to-PII mapping.</param>
    /// <returns>Text with placeholder tokens replaced by original values.</returns>
    string RestorePii(string aiResponseText, PiiRedactionContext context);

    /// <summary>
    /// Returns <c>true</c> if <paramref name="text"/> contains any detectable PII pattern.
    /// Used as a post-redaction validation gate before dispatching to external AI providers.
    /// If this returns <c>true</c> after <see cref="RedactPii"/> the request must be blocked.
    /// </summary>
    /// <param name="text">Text to scan for residual PII.</param>
    bool ContainsPii(string text);
}
