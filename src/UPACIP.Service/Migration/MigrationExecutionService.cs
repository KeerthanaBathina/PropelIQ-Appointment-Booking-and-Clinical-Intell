using System.Data;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.DataAccess;
using UPACIP.Service.Backup;
using UPACIP.Service.Migration.Models;

namespace UPACIP.Service.Migration;

/// <summary>
/// Contract for applying pending EF Core migrations transactionally (US_091 task_001).
/// </summary>
public interface IMigrationExecutionService
{
    /// <summary>
    /// Detects and applies all pending EF Core migrations inside a single PostgreSQL
    /// serializable transaction. If any step fails, the transaction is rolled back and
    /// the database is left in its pre-migration state (AC-2, edge case 1).
    ///
    /// When <c>DryRun = true</c> in options, the generated SQL is logged and returned
    /// without modifying the database.
    ///
    /// When <c>CreatePreMigrationBackup = true</c>, a <c>pg_dump</c> backup is created
    /// before any DDL is applied.
    /// </summary>
    Task<MigrationExecutionResult> ApplyPendingMigrationsAsync(CancellationToken ct = default);
}

/// <summary>
/// Scoped implementation of <see cref="IMigrationExecutionService"/>.
///
/// Transactional guarantees (AC-2):
///   PostgreSQL supports DDL inside transactions — <c>CREATE TABLE</c>, <c>ALTER TABLE</c>,
///   <c>CREATE INDEX</c>, etc., are all rolled back atomically on failure.
///   <c>IsolationLevel.Serializable</c> prevents phantom reads from concurrent schema changes.
///
/// Security (OWASP A03):
///   No user-controlled input reaches any SQL. Only EF Core-generated DDL is executed.
///   Migration IDs are resolved from assembly metadata — not from request parameters.
/// </summary>
public sealed class MigrationExecutionService : IMigrationExecutionService
{
    private readonly ApplicationDbContext                       _context;
    private readonly IOptionsMonitor<MigrationExecutionOptions> _options;
    private readonly IBackupExecutor                            _backupExecutor;
    private readonly IMigrationVerificationService              _verificationService;
    private readonly ICompatibilityGuard                        _compatibilityGuard;
    private readonly ILogger<MigrationExecutionService>         _logger;

    public MigrationExecutionService(
        ApplicationDbContext                        context,
        IOptionsMonitor<MigrationExecutionOptions>  options,
        IBackupExecutor                             backupExecutor,
        IMigrationVerificationService               verificationService,
        ICompatibilityGuard                         compatibilityGuard,
        ILogger<MigrationExecutionService>          logger)
    {
        _context             = context;
        _options             = options;
        _backupExecutor      = backupExecutor;
        _verificationService = verificationService;
        _compatibilityGuard  = compatibilityGuard;
        _logger              = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IMigrationExecutionService
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<MigrationExecutionResult> ApplyPendingMigrationsAsync(CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;
        var sw   = Stopwatch.StartNew();

        // ── (a) Detect pending migrations ─────────────────────────────────────
        var pending  = (await _context.Database.GetPendingMigrationsAsync(ct)).ToList();
        var applied  = (await _context.Database.GetAppliedMigrationsAsync(ct)).ToList();
        var prevVer  = applied.LastOrDefault() ?? "(none)";

        if (pending.Count == 0)
        {
            _logger.LogInformation(
                "MigrationExecutionService: database is up to date. CurrentVersion={Version}.",
                prevVer);
            return MigrationExecutionResult.UpToDate(prevVer, sw.Elapsed);
        }

        _logger.LogInformation(
            "MigrationExecutionService: {Count} pending migration(s) detected. " +
            "PreviousVersion={Previous}, Pending=[{Migrations}].",
            pending.Count, prevVer, string.Join(", ", pending));

        // ── (b) Pre-migration snapshot (row counts for task_002 drift detection) ──
        var preMigrationSnapshot = await TakeRowCountSnapshotAsync(ct);

        // ── (b2) Pre-migration compatibility analysis (AC-4, DR-031) ─────────
        var reports = await _compatibilityGuard.AnalyzePendingMigrationsAsync(ct);
        foreach (var report in reports.Where(r => !r.IsBackwardCompatible))
        {
            foreach (var breaking in report.BreakingChanges)
            {
                _logger.LogWarning(
                    "MIGRATION_COMPATIBILITY: Migration={Migration}, Operation={Op}, " +
                    "Table={Table}, Column={Column}, Severity={Severity}, Suggestion={Suggestion}",
                    breaking.MigrationName, breaking.OperationType,
                    breaking.TableName, breaking.ColumnName ?? "-",
                    breaking.Severity, breaking.ExpandContractSuggestion);
            }
        }

        // ── (c) Pre-migration backup (optional) ───────────────────────────────
        string? backupFile = null;
        if (opts.CreatePreMigrationBackup)
            backupFile = await CreatePreMigrationBackupAsync(ct);

        // ── (d) Dry-run mode ──────────────────────────────────────────────────
        if (opts.DryRun)
        {
            var migrator = _context.GetService<IMigrator>();
            var script   = migrator.GenerateScript(
                fromMigration: prevVer == "(none)" ? null : prevVer,
                toMigration:   null,
                options:       MigrationsSqlGenerationOptions.Default);

            _logger.LogInformation(
                "MigrationExecutionService: DRY RUN — generated SQL for {Count} migration(s):\n{Script}",
                pending.Count, script);

            return MigrationExecutionResult.DryRun(script, prevVer, sw.Elapsed);
        }

        // ── (e) Validate Down() completeness — warn on empty rollback ─────────
        ValidateDownMethods(pending);

        // ── (f) Transactional execution (AC-2) ────────────────────────────────
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(opts.MigrationTimeoutSeconds));

        var appliedThisRun   = new List<string>();
        var currentMigration = "(none)";

        await using var transaction = await _context.Database
            .BeginTransactionAsync(IsolationLevel.Serializable, cts.Token);

        try
        {
            var efMigrator = _context.GetService<IMigrator>();

            foreach (var migrationName in pending)
            {
                currentMigration = migrationName;

                _logger.LogInformation("MIGRATION_APPLYING: Name={MigrationName}", migrationName);

                // Calling MigrateAsync with a specific target applies migrations
                // up to and including that name. Iterating in order applies one at a time.
                await efMigrator.MigrateAsync(migrationName, cts.Token);

                appliedThisRun.Add(migrationName);

                _logger.LogInformation("MIGRATION_APPLIED: Name={MigrationName}", migrationName);
            }

            await transaction.CommitAsync(cts.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Use the original ct (not the timed-out linked one) for the rollback call.
            await transaction.RollbackAsync(ct);

            _logger.LogError(ex,
                "MIGRATION_FAILED: RolledBack=true, FailedMigration={Migration}, Error={Error}",
                currentMigration, ex.Message);

            return MigrationExecutionResult.Failed(
                currentMigration, ex.Message, prevVer, sw.Elapsed, appliedThisRun);
        }

        sw.Stop();

        var appliedAfter = (await _context.Database.GetAppliedMigrationsAsync(ct)).ToList();
        var currVer      = appliedAfter.LastOrDefault() ?? prevVer;

        // ── (g) Post-migration summary log ────────────────────────────────────
        _logger.LogInformation(
            "MIGRATION_COMPLETE: Applied={Count}, Duration={Duration}, " +
            "FromVersion={From}, ToVersion={To}",
            appliedThisRun.Count, sw.Elapsed, prevVer, currVer);

        // ── (h) Post-migration integrity verification (AC-5, DR-032) ─────────
        var verification = await _verificationService
            .VerifyMigrationIntegrityAsync(preMigrationSnapshot, ct);

        if (!verification.OverallPassed)
        {
            _logger.LogWarning(
                "MIGRATION_VERIFICATION_ANOMALY: post-migration verification found issues. " +
                "Errors=[{Errors}], Warnings=[{Warnings}]",
                string.Join("; ", verification.Errors),
                string.Join("; ", verification.Warnings));
        }

        return MigrationExecutionResult.Success(
            appliedThisRun, prevVer, currVer, sw.Elapsed, backupFile);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Down() validation warning (AC-1 awareness)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// For each pending migration, inspects the public <c>DownOperations</c> property and
    /// logs a warning if it contains no rollback operations. Non-blocking — migrations with
    /// empty <c>Down()</c> are still applied. Some seed migrations legitimately have no rollback.
    /// </summary>
    private void ValidateDownMethods(IEnumerable<string> pendingMigrations)
    {
        try
        {
            var migrationsAssembly = _context.GetService<IMigrationsAssembly>();
            var activeProvider     = _context.Database.ProviderName
                                     ?? "Npgsql.EntityFrameworkCore.PostgreSQL";

            foreach (var migrationId in pendingMigrations)
            {
                if (!migrationsAssembly.Migrations.TryGetValue(migrationId, out var migrationType))
                    continue;

                try
                {
                    var migration = migrationsAssembly.CreateMigration(migrationType, activeProvider);

                    // DownOperations is the public property that internally calls protected Down().
                    if (migration.DownOperations.Count == 0)
                    {
                        _logger.LogWarning(
                            "MIGRATION_MISSING_DOWN: {MigrationName} has empty Down() method. " +
                            "Rollback will not undo this migration if applied manually.",
                            migrationId);
                    }
                }
                catch (NotSupportedException)
                {
                    // Developer explicitly threw NotSupportedException — intent is clear, no warning.
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex,
                        "MigrationExecutionService: could not validate Down() for {Migration}.",
                        migrationId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "MigrationExecutionService: Down() validation skipped (assembly resolution failed).");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Pre-migration row count snapshot
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Queries <c>pg_stat_user_tables</c> and logs per-table live row counts as a
    /// pre-migration baseline. Returns the snapshot map for post-migration drift detection.
    /// Failures are swallowed — this is advisory only.
    /// </summary>
    private async Task<Dictionary<string, long>> TakeRowCountSnapshotAsync(CancellationToken ct)
    {
        try
        {
            const string sql =
                "SELECT schemaname || '.' || relname, n_live_tup " +
                "FROM pg_stat_user_tables " +
                "ORDER BY schemaname, relname;";

            var snapshot = new Dictionary<string, long>(StringComparer.Ordinal);

            var conn = _context.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open)
                await conn.OpenAsync(ct);

            await using var cmd    = conn.CreateCommand();
            cmd.CommandText        = sql;
            await using var reader = await cmd.ExecuteReaderAsync(ct);

            var sb = new System.Text.StringBuilder();
            while (await reader.ReadAsync(ct))
            {
                var table = reader.GetString(0);
                var count = reader.GetInt64(1);
                snapshot[table] = count;
                sb.AppendLine($"  {table}: {count:N0} rows");
            }

            _logger.LogInformation(
                "MigrationExecutionService: pre-migration row count snapshot:\n{Snapshot}",
                sb.ToString());

            return snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "MigrationExecutionService: pre-migration snapshot failed — continuing.");
            return new Dictionary<string, long>(StringComparer.Ordinal);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Pre-migration backup
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<string?> CreatePreMigrationBackupAsync(CancellationToken ct)
    {
        try
        {
            _logger.LogInformation("MigrationExecutionService: creating pre-migration backup.");
            var result = await _backupExecutor.ExecuteBackupAsync(isRetry: false, ct: ct);

            if (result.Success)
            {
                _logger.LogInformation(
                    "MigrationExecutionService: pre-migration backup created at {Path}.",
                    result.FilePath);
                return result.FilePath;
            }

            _logger.LogWarning(
                "MigrationExecutionService: pre-migration backup failed ({Error}) — " +
                "continuing with migration.", result.ErrorMessage);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "MigrationExecutionService: pre-migration backup threw — continuing with migration.");
            return null;
        }
    }
}
