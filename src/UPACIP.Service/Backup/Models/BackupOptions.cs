namespace UPACIP.Service.Backup.Models;

/// <summary>
/// Configuration for the <c>DatabaseBackupService</c> (US_088, DR-022).
///
/// All options are bound from the <c>"DatabaseBackup"</c> section in <c>appsettings.json</c>
/// and hot-reloaded at runtime via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/>.
/// </summary>
public sealed class BackupOptions
{
    /// <summary>Configuration section key in appsettings.json.</summary>
    public const string SectionName = "DatabaseBackup";

    /// <summary>
    /// Full path to the <c>pg_dump.exe</c> executable.
    /// Default: standard Windows PostgreSQL 16 installation path.
    /// </summary>
    public string PgDumpPath { get; set; } =
        @"C:\Program Files\PostgreSQL\16\bin\pg_dump.exe";

    /// <summary>
    /// Destination directory for backup files.
    /// Must reside on a volume separate from the database data directory.
    /// Default: dedicated backup volume.
    /// </summary>
    public string BackupDirectory { get; set; } = @"D:\Backups\Database";

    /// <summary>
    /// Local time at which the nightly backup runs, in <c>HH:mm</c> format (24-hour).
    /// Default: <c>"02:00"</c> — 2 AM local time (DR-022, low-traffic window).
    /// </summary>
    public string ScheduleLocalTime { get; set; } = "02:00";

    /// <summary>
    /// Minutes to wait before the single retry attempt after a backup failure (AC-4).
    /// Default: 15 minutes.
    /// </summary>
    public int RetryDelayMinutes { get; set; } = 15;

    /// <summary>
    /// Maximum number of retry attempts after an initial failure (AC-4 — "retries once").
    /// Default: 1.
    /// </summary>
    public int MaxRetries { get; set; } = 1;

    /// <summary>
    /// Backup storage disk usage threshold (%) above which the backup is skipped and a
    /// critical storage alert is emitted (edge case 1).
    /// Default: 80.0 (80%).
    /// </summary>
    public double DiskSpaceThresholdPercent { get; set; } = 80.0;

    /// <summary>
    /// Backup storage disk usage (%) above which a warning is emitted for proactive monitoring.
    /// Must be less than <see cref="DiskSpaceThresholdPercent"/>.
    /// Default: 70.0 (70%).
    /// </summary>
    public double DiskSpaceWarningPercent { get; set; } = 70.0;

    /// <summary>
    /// PostgreSQL database name to back up.
    /// Default: <c>"upacip"</c>.
    /// </summary>
    public string DatabaseName { get; set; } = "upacip";

    /// <summary>
    /// pg_dump output format: <c>"custom"</c> (compressed, selective restore) or
    /// <c>"plain"</c> (SQL text).
    /// Default: <c>"custom"</c>.
    /// </summary>
    public string BackupFormat { get; set; } = "custom";

    /// <summary>
    /// PostgreSQL host. Extracted from the connection string or configured explicitly.
    /// Default: <c>"localhost"</c>.
    /// </summary>
    public string DbHost { get; set; } = "localhost";

    /// <summary>
    /// PostgreSQL port. Default: <c>5432</c>.
    /// </summary>
    public int DbPort { get; set; } = 5432;

    /// <summary>
    /// PostgreSQL username for backup connection.
    /// Default: <c>"postgres"</c>.
    /// Stored in configuration — password is sourced separately and never in command-line args.
    /// </summary>
    public string DbUsername { get; set; } = "postgres";

    /// <summary>
    /// PostgreSQL password for the backup connection.
    /// Set via <c>PGPASSWORD</c> environment variable on the child process —
    /// never passed as a command-line argument (OWASP A02 — credential exposure prevention).
    /// Store in environment-specific appsettings or secrets management (not committed to source).
    /// </summary>
    public string DbPassword { get; set; } = string.Empty;
}
