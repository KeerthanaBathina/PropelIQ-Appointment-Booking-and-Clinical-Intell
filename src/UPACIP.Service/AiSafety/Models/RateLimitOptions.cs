namespace UPACIP.Service.AiSafety.Models;

/// <summary>
/// Configuration options for the AI rate limiter, bound from the
/// <c>AiRateLimiting</c> section of <c>appsettings.json</c>
/// (US_079 task_003, AC-4, AIR-S08, TR-027).
/// </summary>
public sealed class RateLimitOptions
{
    /// <summary>Configuration section key used for DI binding.</summary>
    public const string SectionName = "AiRateLimiting";

    /// <summary>Maximum AI requests per sliding window for Patient role (default: 100/hr).</summary>
    public int PatientLimitPerHour { get; init; } = 100;

    /// <summary>Maximum AI requests per sliding window for Staff role (default: 500/hr).</summary>
    public int StaffLimitPerHour { get; init; } = 500;

    /// <summary>Maximum AI requests per sliding window for Admin role (default: 1000/hr).</summary>
    public int AdminLimitPerHour { get; init; } = 1000;

    /// <summary>
    /// Sliding window duration in minutes (default: 60).
    /// Increasing this widens the window without changing the per-hour limit semantics.
    /// </summary>
    public int WindowSizeMinutes { get; init; } = 60;

    /// <summary>
    /// Duration in minutes that an admin-set temporary override remains in effect (default: 120).
    /// Hard capped to 480 (8 hours) by the admin controller validation.
    /// </summary>
    public int TemporaryOverrideDurationMinutes { get; init; } = 120;
}
