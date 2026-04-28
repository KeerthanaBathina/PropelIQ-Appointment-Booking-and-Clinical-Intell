using UPACIP.Service.AiSafety.Models;

namespace UPACIP.Service.AiSafety;

/// <summary>
/// Detects and sanitises prompt injection attempts in user-supplied text before
/// it is included in AI prompts (US_079 task_001, AIR-S06, AIR-S04).
///
/// <para>
/// Detection uses a layered pattern-matching approach covering four injection
/// categories: RoleImpersonation, InstructionOverride, DataExfiltration, and
/// DelimiterInjection.  Patterns are loaded from an external configuration file
/// (<c>config/prompt-injection-patterns.json</c>) via
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/> with
/// hot-reload support so new patterns take effect without restart.
/// </para>
///
/// <para>
/// A medical context scoring heuristic suppresses false positives: when a pattern
/// match falls within a ±50-character clinical context window containing
/// <see cref="MedicalTermAllowlist"/> terms or ICD-10 codes the match is logged
/// as a false positive and skipped (US_079 edge case).
/// </para>
///
/// <para>
/// All detection events are logged to the audit trail per AIR-S04 — the
/// <see cref="SanitizeAsync"/> method logs injection category, risk score, and
/// sanitised input.  Raw unsanitised attack payloads are <b>never</b> logged.
/// </para>
///
/// <para>Implementation is Singleton (thread-safe; stateless apart from cached compiled regexes).</para>
/// </summary>
public interface IPromptInjectionDetector
{
    /// <summary>
    /// Scans <paramref name="userInput"/> against all configured injection patterns
    /// and returns an <see cref="InjectionDetectionResult"/> describing what was found.
    ///
    /// <para>
    /// Each regex executes with a 100 ms timeout to prevent ReDoS attacks.
    /// Patterns are evaluated in severity order (Critical first).
    /// </para>
    /// </summary>
    /// <param name="userInput">Raw user-supplied text to scan.</param>
    /// <param name="cancellationToken">Propagates cancellation from the caller.</param>
    /// <returns>
    /// Detection result containing matched patterns, sanitised text, and aggregate risk score.
    /// </returns>
    Task<InjectionDetectionResult> DetectAsync(
        string            userInput,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects injection patterns in <paramref name="userInput"/>, emits a structured
    /// audit log event per AIR-S04 (injection category and sanitised text — never raw
    /// attack payload), and returns the full <see cref="InjectionDetectionResult"/>.
    ///
    /// <para>
    /// Callers use <see cref="InjectionDetectionResult.RiskScore"/> to decide whether
    /// to block (≥ 0.8) or proceed with <see cref="InjectionDetectionResult.SanitizedText"/>
    /// (&lt; 0.8).
    /// </para>
    /// </summary>
    /// <param name="userInput">Raw user-supplied text to sanitise.</param>
    /// <param name="userId">Caller's user ID for audit log correlation (AIR-S04).</param>
    /// <param name="cancellationToken">Propagates cancellation from the caller.</param>
    Task<InjectionDetectionResult> SanitizeAsync(
        string            userInput,
        string            userId,
        CancellationToken cancellationToken = default);
}
