namespace UPACIP.Service.Backup.Models;

/// <summary>
/// Configuration for the WAL archival monitoring service (US_090, AC-1, DR-027, NFR-024).
/// Bound from the <c>"WalArchival"</c> configuration section.
/// PostgreSQL credentials are shared from <see cref="BackupOptions"/> at runtime
/// (same pg tools / same PostgreSQL server).
/// </summary>
public sealed class WalArchivalOptions
{
    /// <summary>Configuration section key used with <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/>.</summary>
    public const string SectionName = "WalArchival";

    /// <summary>
    /// Directory where PostgreSQL writes archived WAL segments via <c>archive_command</c>.
    /// Should be on a separate volume from the PostgreSQL data directory.
    /// Default: dedicated WAL backup volume.
    /// </summary>
    public string WalArchiveDirectory { get; init; } = @"D:\Backups\WAL";

    /// <summary>
    /// Interval in minutes between forced <c>pg_switch_wal()</c> calls.
    /// Default: 15 minutes (AC-1 — "every 15 minutes").
    /// </summary>
    public int SwitchWalIntervalMinutes { get; init; } = 15;

    /// <summary>
    /// Interval in minutes between WAL archival health monitoring checks.
    /// Default: 5 minutes (checks are lightweight — just directory scan + one psql query).
    /// </summary>
    public int MonitoringCheckIntervalMinutes { get; init; } = 5;

    /// <summary>
    /// Number of minutes without a new archived WAL segment before a stall alert is emitted.
    /// Default: 20 minutes (5-minute margin above the 15-minute switch interval).
    /// </summary>
    public int MaxWalArchivalDelayMinutes { get; init; } = 20;

    /// <summary>
    /// Number of days to retain WAL segments in the archive directory.
    /// Aligned with the daily backup retention window from US_088 (default 30 days).
    /// WAL segments covering the retention window of the oldest retained backup are never deleted.
    /// </summary>
    public int WalRetentionDays { get; init; } = 30;

    /// <summary>
    /// Full path to the <c>psql.exe</c> executable.
    /// Used for <c>pg_switch_wal()</c> calls and <c>pg_stat_archiver</c> queries.
    /// Default: standard Windows PostgreSQL 16 installation path.
    /// </summary>
    public string PsqlPath { get; init; } =
        @"C:\Program Files\PostgreSQL\16\bin\psql.exe";

    /// <summary>
    /// Full path to the <c>pg_waldump.exe</c> executable.
    /// Used for WAL segment integrity validation (edge case 1).
    /// Default: standard Windows PostgreSQL 16 installation path.
    /// </summary>
    public string PgWaldumpPath { get; init; } =
        @"C:\Program Files\PostgreSQL\16\bin\pg_waldump.exe";
}
