namespace UPACIP.Service.Backup.Models;

/// <summary>
/// Configuration for the three-tier backup retention policy (US_088, AC-2, DR-023).
///
/// Tiers (highest retention wins when a backup qualifies for multiple):
/// <list type="bullet">
///   <item><b>Monthly</b> — backup taken on <see cref="MonthlyBackupDayOfMonth"/> (default: 1st). Retained for 365 days.</item>
///   <item><b>Weekly</b>  — backup taken on <see cref="WeeklyBackupDay"/> (default: Sunday). Retained for 90 days.</item>
///   <item><b>Daily</b>   — all other backups. Retained for 30 days.</item>
/// </list>
///
/// Bound from the <c>"BackupRetention"</c> section in <c>appsettings.json</c>.
/// Hot-reloaded via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/>.
/// </summary>
public sealed class BackupRetentionOptions
{
    /// <summary>Configuration section key in appsettings.json.</summary>
    public const string SectionName = "BackupRetention";

    /// <summary>
    /// Number of days to retain daily-tier backups (AC-2 — "30 days").
    /// Default: 30.
    /// </summary>
    public int DailyRetentionDays { get; set; } = 30;

    /// <summary>
    /// Number of days to retain weekly-tier backups (AC-2 — "90 days").
    /// Default: 90.
    /// </summary>
    public int WeeklyRetentionDays { get; set; } = 90;

    /// <summary>
    /// Number of days to retain monthly-tier backups (AC-2 — "1 year = 365 days").
    /// Default: 365.
    /// </summary>
    public int MonthlyRetentionDays { get; set; } = 365;

    /// <summary>
    /// Day of week on which a backup is promoted to the weekly tier.
    /// Default: <see cref="DayOfWeek.Sunday"/>.
    /// </summary>
    public DayOfWeek WeeklyBackupDay { get; set; } = DayOfWeek.Sunday;

    /// <summary>
    /// Day of month (1–28) on which a backup is promoted to the monthly tier.
    /// Use values ≤ 28 to ensure every month has a qualifying monthly backup regardless
    /// of month length. Default: 1 (1st of each month).
    /// </summary>
    public int MonthlyBackupDayOfMonth { get; set; } = 1;
}
