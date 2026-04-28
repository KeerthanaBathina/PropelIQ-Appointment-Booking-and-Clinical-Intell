namespace UPACIP.Service.AiSafety.Models;

/// <summary>Severity levels for prompt injection patterns (US_079 task_001, AIR-S06).</summary>
public enum InjectionSeverity
{
    Low,
    Medium,
    High,
    Critical,
}

/// <summary>
/// Injection category classifications for audit logging and risk scoring
/// (US_079 task_001, AIR-S06).
/// </summary>
public enum InjectionCategory
{
    RoleImpersonation,
    InstructionOverride,
    DataExfiltration,
    DelimiterInjection,
}

/// <summary>
/// Definition of a single prompt injection detection pattern loaded from
/// <c>config/prompt-injection-patterns.json</c> and bound via
/// <see cref="Microsoft.Extensions.Options.IOptionsMonitor{TOptions}"/>
/// (US_079 task_001, AIR-S06).
///
/// <para>
/// The <see cref="Category"/> and <see cref="Severity"/> fields are string names matching
/// the corresponding enum values (<see cref="InjectionCategory"/> and
/// <see cref="InjectionSeverity"/>) so that the JSON configuration file remains
/// human-readable without numeric codes.
/// </para>
/// </summary>
public sealed class InjectionPattern
{
    /// <summary>
    /// Injection category name — must match an <see cref="InjectionCategory"/> enum value:
    /// RoleImpersonation, InstructionOverride, DataExfiltration, or DelimiterInjection.
    /// </summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// Regular expression pattern string used for compiled detection.
    /// Must be a valid .NET regex; invalid patterns are skipped with a warning at startup.
    /// </summary>
    public string RegexPattern { get; init; } = string.Empty;

    /// <summary>
    /// Severity name — must match an <see cref="InjectionSeverity"/> enum value:
    /// Low, Medium, High, or Critical.
    /// </summary>
    public string Severity { get; init; } = string.Empty;

    /// <summary>Human-readable description used in audit log entries (AIR-S04).</summary>
    public string Description { get; init; } = string.Empty;
}
