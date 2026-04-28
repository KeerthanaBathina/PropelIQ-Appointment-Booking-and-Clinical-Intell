namespace UPACIP.Service.Migration.Models;

/// <summary>
/// Result of a single migration execution pipeline run (US_091 task_001, AC-2, AC-3, DR-028).
/// Use the static factory methods to construct instances.
/// </summary>
public sealed record MigrationExecutionResult
{
    // ── Status ───────────────────────────────────────────────────────────────

    /// <summary>
    /// One of: <c>"UpToDate"</c>, <c>"Applied"</c>, <c>"Failed"</c>, <c>"DryRun"</c>.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>Number of migrations applied. Zero when <c>Status = "UpToDate"</c>.</summary>
    public int AppliedCount { get; init; }

    /// <summary>Ordered list of migration names applied during this run.</summary>
    public List<string> AppliedMigrations { get; init; } = [];

    // ── Failure details ───────────────────────────────────────────────────────

    /// <summary>Name of the migration that caused the failure. <c>null</c> on success.</summary>
    public string? FailedMigration { get; init; }

    /// <summary>Error message from the failed migration. <c>null</c> on success.</summary>
    public string? ErrorMessage { get; init; }

    // ── Timing ────────────────────────────────────────────────────────────────

    /// <summary>Total wall-clock time for the migration run (including pre-migration backup).</summary>
    public TimeSpan Duration { get; init; }

    // ── Version tracking ─────────────────────────────────────────────────────

    /// <summary>
    /// Last applied migration name before this run.
    /// <c>"(none)"</c> when the database had no applied migrations.
    /// </summary>
    public string PreviousVersion { get; init; } = "(none)";

    /// <summary>
    /// Last applied migration name after this run.
    /// Same as <see cref="PreviousVersion"/> when rolled back or up to date.
    /// </summary>
    public string CurrentVersion { get; init; } = "(none)";

    // ── Pre-migration backup ─────────────────────────────────────────────────

    /// <summary>
    /// Path of the <c>pg_dump</c> backup created before this migration run.
    /// <c>null</c> when backup is disabled or no migrations were pending.
    /// </summary>
    public string? PreMigrationBackupFile { get; init; }

    // ── Dry-run script ────────────────────────────────────────────────────────

    /// <summary>Generated SQL script in dry-run mode. <c>null</c> during normal execution.</summary>
    public string? DryRunScript { get; init; }

    // ─────────────────────────────────────────────────────────────────────────
    // Static factory methods
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Returns a result indicating the database is already up to date.</summary>
    public static MigrationExecutionResult UpToDate(string currentVersion, TimeSpan duration) =>
        new()
        {
            Status          = "UpToDate",
            CurrentVersion  = currentVersion,
            PreviousVersion = currentVersion,
            Duration        = duration,
        };

    /// <summary>Returns a successful migration result.</summary>
    public static MigrationExecutionResult Success(
        List<string> appliedMigrations,
        string       previousVersion,
        string       currentVersion,
        TimeSpan     duration,
        string?      preMigrationBackupFile = null) =>
        new()
        {
            Status                 = "Applied",
            AppliedCount           = appliedMigrations.Count,
            AppliedMigrations      = appliedMigrations,
            PreviousVersion        = previousVersion,
            CurrentVersion         = currentVersion,
            Duration               = duration,
            PreMigrationBackupFile = preMigrationBackupFile,
        };

    /// <summary>Returns a failed migration result. Database was rolled back to PreviousVersion.</summary>
    public static MigrationExecutionResult Failed(
        string       failedMigration,
        string       errorMessage,
        string       previousVersion,
        TimeSpan     duration,
        List<string>? partiallyApplied = null) =>
        new()
        {
            Status            = "Failed",
            FailedMigration   = failedMigration,
            ErrorMessage      = errorMessage,
            PreviousVersion   = previousVersion,
            CurrentVersion    = previousVersion,   // rolled back — version unchanged
            Duration          = duration,
            AppliedMigrations = partiallyApplied ?? [],
        };

    /// <summary>Returns a dry-run result containing the generated SQL script.</summary>
    public static MigrationExecutionResult DryRun(
        string   generatedScript,
        string   previousVersion,
        TimeSpan duration) =>
        new()
        {
            Status          = "DryRun",
            DryRunScript    = generatedScript,
            PreviousVersion = previousVersion,
            CurrentVersion  = previousVersion,
            Duration        = duration,
        };
}
