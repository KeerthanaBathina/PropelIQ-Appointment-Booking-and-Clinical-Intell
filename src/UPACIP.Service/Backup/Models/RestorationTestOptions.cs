namespace UPACIP.Service.Backup.Models;

/// <summary>
/// Configuration for the quarterly backup restoration test pipeline (AC-3, DR-026).
/// Bound from the <c>"RestorationTest"</c> configuration section.
/// Test database credentials are supplied via environment variables — never committed
/// to source control (OWASP A02).
/// </summary>
public sealed class RestorationTestOptions
{
    /// <summary>Configuration section key used with <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/>.</summary>
    public const string SectionName = "RestorationTest";

    /// <summary>
    /// Full path to the <c>pg_restore.exe</c> executable.
    /// Default: standard Windows PostgreSQL 16 installation path.
    /// </summary>
    public string PgRestorePath { get; init; } =
        @"C:\Program Files\PostgreSQL\16\bin\pg_restore.exe";

    /// <summary>
    /// Full path to the <c>psql.exe</c> executable (used for DROP/CREATE DATABASE commands).
    /// Default: standard Windows PostgreSQL 16 installation path.
    /// </summary>
    public string PsqlPath { get; init; } =
        @"C:\Program Files\PostgreSQL\16\bin\psql.exe";

    /// <summary>
    /// Name of the isolated test database for restoration validation.
    /// MUST NOT match the production database name — validated at runtime.
    /// </summary>
    public string TestDatabaseName { get; init; } = "upacip_restore_test";

    /// <summary>Host of the test database server.</summary>
    public string TestDatabaseHost { get; init; } = "localhost";

    /// <summary>Port of the test database server.</summary>
    public int TestDatabasePort { get; init; } = 5432;

    /// <summary>
    /// Username for the test database connection.
    /// Must have CREATEDB, CONNECT, and schema-create privileges.
    /// </summary>
    public string TestDatabaseUsername { get; init; } = "upacip_test";

    /// <summary>
    /// Test database password sourced exclusively from environment variable
    /// <c>RestorationTest__TestDatabasePassword</c> — never stored in appsettings.json.
    /// </summary>
    public string TestDatabasePassword { get; init; } = string.Empty;

    /// <summary>
    /// Number of days before the quarterly deadline to begin emitting "DueSoon" alerts.
    /// Default: 14 days.
    /// </summary>
    public int QuarterlyAlertDaysBeforeDue { get; init; } = 14;

    /// <summary>
    /// Maximum time allowed for <c>pg_restore</c> to complete before the operation times out.
    /// Default: 120 minutes.
    /// </summary>
    public int MaxRestorationTimeoutMinutes { get; init; } = 120;
}
