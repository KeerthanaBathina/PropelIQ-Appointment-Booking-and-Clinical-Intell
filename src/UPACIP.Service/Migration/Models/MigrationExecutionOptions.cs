namespace UPACIP.Service.Migration.Models;

/// <summary>
/// Configuration for the migration execution pipeline (US_091 task_001, DR-028, DR-029).
/// Bound from <c>"MigrationExecution"</c> in <c>appsettings.json</c>.
/// Credentials are always supplied via environment variables — never here.
/// </summary>
public sealed class MigrationExecutionOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "MigrationExecution";

    /// <summary>
    /// Maximum seconds allowed for the entire migration batch before a
    /// <see cref="OperationCanceledException"/> is raised and the transaction rolls back.
    /// Default: 300 (5 minutes).
    /// </summary>
    public int MigrationTimeoutSeconds { get; init; } = 300;

    /// <summary>
    /// When <c>true</c>, triggers a full <c>pg_dump</c> backup via <c>IBackupExecutor</c>
    /// before applying any migrations. Provides a safety net for production rollback.
    /// Default: <c>true</c>.
    /// </summary>
    public bool CreatePreMigrationBackup { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, logs each individual SQL command emitted during migration execution.
    /// Default: <c>true</c>.
    /// </summary>
    public bool EnableDetailedLogging { get; init; } = true;

    /// <summary>
    /// When <c>true</c>, generates and logs the migration SQL without executing it.
    /// No changes are made to the database.
    /// Default: <c>false</c>.
    /// </summary>
    public bool DryRun { get; init; } = false;
}
