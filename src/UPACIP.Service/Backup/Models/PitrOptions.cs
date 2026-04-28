namespace UPACIP.Service.Backup.Models;

/// <summary>
/// Configuration for the point-in-time recovery pipeline (US_090, AC-2, AC-4, DR-027, NFR-025).
/// Bound from the <c>"PitrRecovery"</c> configuration section.
/// Recovery database credentials are supplied via environment variables — never committed
/// to source control (OWASP A02).
/// </summary>
public sealed class PitrOptions
{
    /// <summary>Configuration section key used with <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/>.</summary>
    public const string SectionName = "PitrRecovery";

    /// <summary>
    /// Name of the isolated database used for PITR restoration.
    /// MUST NOT match the production database name — validated at runtime.
    /// </summary>
    public string RecoveryDatabaseName { get; init; } = "upacip_pitr_recovery";

    /// <summary>
    /// Temporary PostgreSQL data directory for the recovery instance.
    /// Used when starting a standalone pg instance for WAL replay.
    /// </summary>
    public string RecoveryDataDirectory { get; init; } = @"D:\Recovery\PgData";

    /// <summary>
    /// Full path to <c>pg_basebackup.exe</c>.
    /// Default: standard Windows PostgreSQL 16 installation path.
    /// </summary>
    public string PgBasebackupPath { get; init; } =
        @"C:\Program Files\PostgreSQL\16\bin\pg_basebackup.exe";

    /// <summary>
    /// Full path to <c>pg_restore.exe</c>.
    /// Default: standard Windows PostgreSQL 16 installation path.
    /// </summary>
    public string PgRestorePath { get; init; } =
        @"C:\Program Files\PostgreSQL\16\bin\pg_restore.exe";

    /// <summary>
    /// Full path to <c>pg_ctl.exe</c> for managing the recovery PostgreSQL instance.
    /// Default: standard Windows PostgreSQL 16 installation path.
    /// </summary>
    public string PgCtlPath { get; init; } =
        @"C:\Program Files\PostgreSQL\16\bin\pg_ctl.exe";

    /// <summary>
    /// Full path to <c>psql.exe</c> for admin commands (DROP/CREATE DATABASE, pg_is_in_recovery).
    /// Default: standard Windows PostgreSQL 16 installation path.
    /// </summary>
    public string PsqlPath { get; init; } =
        @"C:\Program Files\PostgreSQL\16\bin\psql.exe";

    /// <summary>
    /// TCP port for the isolated recovery PostgreSQL instance.
    /// Must differ from the production port (default 5432).
    /// </summary>
    public int RecoveryPort { get; init; } = 5433;

    /// <summary>
    /// Maximum allowed duration (minutes) for the full PITR pipeline before timeout.
    /// Default: 240 minutes (4 hours — aligned with NFR-025 RTO target).
    /// </summary>
    public int MaxRecoveryTimeoutMinutes { get; init; } = 240;

    /// <summary>
    /// Whether to automatically stop the recovery instance and remove the recovery data
    /// directory after post-recovery validation completes.
    /// Default: <c>false</c> — keep recovery instance running on <see cref="RecoveryPort"/>
    /// for admin inspection before promoting or discarding.
    /// </summary>
    public bool AutoDropRecoveryDb { get; init; } = false;

    /// <summary>
    /// Password for the recovery database connection.
    /// Set exclusively via environment variable <c>PitrRecovery__RecoveryPassword</c>
    /// — never stored in <c>appsettings.json</c> (OWASP A02).
    /// </summary>
    public string RecoveryPassword { get; init; } = string.Empty;
}
