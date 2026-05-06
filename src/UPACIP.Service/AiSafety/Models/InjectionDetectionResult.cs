namespace UPACIP.Service.AiSafety.Models;

/// <summary>
/// Represents a single pattern match found during a prompt injection scan
/// (US_079 task_001, AIR-S06).
/// </summary>
public sealed class DetectedPattern
{
    /// <summary>
    /// Injection category (e.g., "RoleImpersonation").
    /// Matches an <see cref="InjectionCategory"/> enum value name.
    /// </summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// The matched text excerpt, truncated to 50 characters for safe audit logging
    /// (never the full raw payload per AIR-S04).
    /// </summary>
    public string MatchedText { get; init; } = string.Empty;

    /// <summary>Zero-based character offset in the original input where the match begins.</summary>
    public int MatchPosition { get; init; }

    /// <summary>
    /// Severity of the matched pattern (e.g., "Critical", "High", "Medium", "Low").
    /// Matches an <see cref="InjectionSeverity"/> enum value name.
    /// </summary>
    public string Severity { get; init; } = string.Empty;

    /// <summary>Human-readable description of the detected pattern for audit logs.</summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>
/// Result of a prompt injection detection scan (US_079 task_001, AIR-S06).
///
/// <para>
/// The <see cref="SanitizedText"/> always contains the cleaned version of the
/// input regardless of whether injection was detected — callers should always
/// use <see cref="SanitizedText"/> for downstream processing.
/// </para>
/// </summary>
public sealed class InjectionDetectionResult
{
    /// <summary>
    /// <c>true</c> when at least one injection pattern was matched that was not
    /// suppressed by the medical context false-positive heuristic (US_079 edge case).
    /// </summary>
    public bool IsInjectionDetected { get; init; }

    /// <summary>
    /// All patterns matched in this scan that were not suppressed as medical
    /// false positives. Empty when <see cref="IsInjectionDetected"/> is <c>false</c>.
    /// </summary>
    public IReadOnlyList<DetectedPattern> DetectedPatterns { get; init; } =
        Array.Empty<DetectedPattern>();

    /// <summary>
    /// The sanitised version of the input: dangerous patterns replaced with
    /// <c>[SANITIZED]</c> placeholders, ASCII control characters stripped
    /// (excluding LF, CR, TAB), and Unicode normalised to NFC form.
    /// </summary>
    public string SanitizedText { get; init; } = string.Empty;

    /// <summary>
    /// Aggregate risk score in [0.0, 1.0] computed as the maximum severity weight
    /// among all non-false-positive detected patterns.
    /// Severity weights: Critical=1.0, High=0.8, Medium=0.5, Low=0.2.
    /// 0.0 when no injection was detected.
    /// </summary>
    public float RiskScore { get; init; }

    /// <summary>
    /// <c>true</c> when at least one pattern match was suppressed by the medical
    /// context heuristic (e.g., "review of systems" or "ignore previous medication").
    /// Used by callers to log false-positive suppression events for pattern tuning.
    /// </summary>
    public bool WasMedicalFalsePositive { get; init; }
}
