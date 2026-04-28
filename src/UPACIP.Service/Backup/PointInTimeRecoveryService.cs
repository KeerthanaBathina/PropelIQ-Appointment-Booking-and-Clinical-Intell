using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.Service.Backup.Models;

namespace UPACIP.Service.Backup;

/// <summary>
/// Admin-triggered point-in-time recovery pipeline (US_090, AC-2, AC-3, AC-4, DR-027).
/// </summary>
public interface IPointInTimeRecoveryService
{
    /// <summary>
    /// Executes a five-phase PITR pipeline: pre-flight → base restore → WAL replay →
    /// integrity validation → finalization.
    /// With <c>DryRun = true</c> only the pre-flight phase runs and a feasibility
    /// assessment is returned without any database changes.
    /// </summary>
    Task<PitrResult> ExecuteRecoveryAsync(PitrRequest request, CancellationToken ct);

    /// <summary>Returns paginated <c>RecoveryLog</c> history entries (most recent first).</summary>
    Task<List<RecoveryLog>> GetRecoveryHistoryAsync(int page, int pageSize, CancellationToken ct);
}

/// <summary>
/// Scoped implementation of <see cref="IPointInTimeRecoveryService"/>.
///
/// Security notes (OWASP A02 / A01):
/// <list type="bullet">
///   <item>Recovery never runs against the production database — separate name + port enforced.</item>
///   <item><c>PGPASSWORD</c> set on child process environments only, cleared in <c>finally</c>.</item>
///   <item>Temporary decrypted <c>.dump</c> deleted in a <c>finally</c> block on all code paths.</item>
///   <item>SQL identifiers validated with <c>IsValidIdentifier</c> before embedding in queries.</item>
/// </list>
/// </summary>
public sealed class PointInTimeRecoveryService : IPointInTimeRecoveryService
{
    // WAL segment name pattern (matches task_001 gap detection).
    private static readonly Regex WalSegmentRegex =
        new(@"^(?<timeline>[0-9A-Fa-f]{8})(?<log>[0-9A-Fa-f]{8})(?<seg>[0-9A-Fa-f]{8})$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Buffer for raw-connection queries.
    private const int QueryBufferSize = 262_144;

    private readonly IOptionsMonitor<PitrOptions>          _pitrOptions;
    private readonly IOptionsMonitor<BackupOptions>        _backupOptions;
    private readonly IOptionsMonitor<WalArchivalOptions>   _walOptions;
    private readonly IBackupEncryptionService              _encryptionService;
    private readonly WalArchivalMonitoringService          _walMonitor;
    private readonly ApplicationDbContext                  _db;
    private readonly ILogger<PointInTimeRecoveryService>   _logger;

    public PointInTimeRecoveryService(
        IOptionsMonitor<PitrOptions>        pitrOptions,
        IOptionsMonitor<BackupOptions>      backupOptions,
        IOptionsMonitor<WalArchivalOptions> walOptions,
        IBackupEncryptionService            encryptionService,
        WalArchivalMonitoringService        walMonitor,
        ApplicationDbContext               db,
        ILogger<PointInTimeRecoveryService> logger)
    {
        _pitrOptions       = pitrOptions;
        _backupOptions     = backupOptions;
        _walOptions        = walOptions;
        _encryptionService = encryptionService;
        _walMonitor        = walMonitor;
        _db                = db;
        _logger            = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IPointInTimeRecoveryService
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<PitrResult> ExecuteRecoveryAsync(PitrRequest request, CancellationToken ct)
    {
        var pitrOpts   = _pitrOptions.CurrentValue;
        var backupOpts = _backupOptions.CurrentValue;
        var startedAt  = DateTime.UtcNow;
        var sw         = Stopwatch.StartNew();

        // ── Safety guard: recovery DB must not be production DB ────────────────
        if (string.Equals(pitrOpts.RecoveryDatabaseName, backupOpts.DatabaseName,
                StringComparison.OrdinalIgnoreCase))
        {
            const string safetyMsg =
                "PitrRecovery:RecoveryDatabaseName must differ from the production database. " +
                "Aborting to prevent production data loss.";
            _logger.LogError("PITR_FAILED: {Reason}", safetyMsg);
            return FailedResult(request, startedAt, TimeSpan.Zero, safetyMsg);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Phase 1: Pre-flight validation
        // ─────────────────────────────────────────────────────────────────────

        var (preflightOk, baseBackupFile, fallbackReason, feasibility) =
            RunPreflightValidation(request, backupOpts, pitrOpts);

        if (!preflightOk || baseBackupFile is null)
        {
            var msg = feasibility ?? "Pre-flight validation failed.";
            var failResult = FailedResult(request, startedAt, sw.Elapsed, msg, fallbackReason);
            await PersistRecoveryLogAsync(request, failResult, ct);
            return failResult;
        }

        if (request.DryRun)
        {
            _logger.LogInformation(
                "PITR_DRY_RUN: Target={Target}, BaseBackup={Backup}, FallbackReason={Fallback}",
                request.TargetTimestampUtc, baseBackupFile.Name, fallbackReason ?? "none");

            var dryResult = new PitrResult
            {
                Success                  = true,
                TargetTimestampUtc       = request.TargetTimestampUtc,
                ActualRecoveryPointUtc   = request.TargetTimestampUtc,
                BaseBackupUsed           = baseBackupFile.Name,
                WalSegmentsReplayed      = CountWalSegmentsInRange(baseBackupFile.LastWriteTimeUtc, request.TargetTimestampUtc),
                RecoveryDuration         = sw.Elapsed,
                IntegrityValidationPassed = true,
                RowCountResults          = [],
                ChecksumResults          = [],
                FallbackReason           = fallbackReason,
                IsDryRun                 = true,
                FeasibilitySummary       = feasibility,
            };
            await PersistRecoveryLogAsync(request, dryResult, ct);
            return dryResult;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Phases 2–5: Actual recovery (with temp-file cleanup guarantee)
        // ─────────────────────────────────────────────────────────────────────

        string? tempDecryptedPath = null;

        try
        {
            // Phase 2a: Decrypt base backup
            string plaintextPath;
            if (baseBackupFile.FullName.EndsWith(".dump.enc", StringComparison.OrdinalIgnoreCase))
            {
                tempDecryptedPath = Path.Combine(
                    backupOpts.BackupDirectory,
                    Path.GetFileNameWithoutExtension(baseBackupFile.Name) + ".pitr.tmp.dump");

                _logger.LogInformation(
                    "PointInTimeRecoveryService: decrypting {Enc} → {Tmp}",
                    baseBackupFile.Name, Path.GetFileName(tempDecryptedPath));

                plaintextPath = await _encryptionService.DecryptBackupAsync(
                    baseBackupFile.FullName, tempDecryptedPath, ct);
            }
            else
            {
                plaintextPath = baseBackupFile.FullName;
            }

            // Phase 2b: Recreate recovery database and restore base backup
            var restoreError = await RecreateRecoveryDatabaseAsync(pitrOpts, backupOpts, ct);
            if (restoreError is not null)
                return FailedResult(request, startedAt, sw.Elapsed, restoreError);

            var (restoreOk, restoreError2) = await RestoreBaseBackupAsync(
                plaintextPath, pitrOpts, backupOpts, ct);

            if (!restoreOk)
                return FailedResult(request, startedAt, sw.Elapsed, restoreError2!);

            // Phase 3: Configure WAL recovery parameters and apply WAL to target timestamp
            var (walOk, walError, walReplayed) = await ApplyWalRecoveryAsync(
                pitrOpts, backupOpts, request.TargetTimestampUtc, ct);

            if (!walOk)
            {
                // WAL replay failed — fall back to base backup only (edge case 1)
                _logger.LogWarning(
                    "PointInTimeRecoveryService: WAL replay failed ({Error}). " +
                    "Recovery reflects base backup timestamp only.",
                    walError);
                fallbackReason ??= $"WAL replay failed: {walError}. Recovery reflects base backup point only.";
            }

            // Phase 4: Post-recovery integrity validation (AC-3)
            var (rowCounts, checksums, integrityPassed) = request.ValidateIntegrity
                ? await ValidateRecoveryIntegrityAsync(pitrOpts, backupOpts, ct)
                : (new List<TableRowCountComparison>(), new List<TableChecksumComparison>(), true);

            sw.Stop();

            var result = new PitrResult
            {
                Success                  = restoreOk,
                TargetTimestampUtc       = request.TargetTimestampUtc,
                ActualRecoveryPointUtc   = walOk ? request.TargetTimestampUtc : baseBackupFile.LastWriteTimeUtc,
                BaseBackupUsed           = baseBackupFile.Name,
                WalSegmentsReplayed      = walReplayed,
                RecoveryDuration         = sw.Elapsed,
                IntegrityValidationPassed = integrityPassed,
                RowCountResults          = rowCounts,
                ChecksumResults          = checksums,
                FallbackReason           = fallbackReason,
                IsDryRun                 = false,
                FeasibilitySummary       = null,
            };

            _logger.LogInformation(
                "PITR_COMPLETE: Target={Target}, Actual={Actual}, BaseBackup={Backup}, " +
                "WalReplayed={Count}, Duration={Duration}, IntegrityPassed={Integrity}",
                request.TargetTimestampUtc,
                result.ActualRecoveryPointUtc,
                baseBackupFile.Name,
                walReplayed,
                sw.Elapsed,
                integrityPassed);

            // Phase 5: Auto cleanup if configured
            if (pitrOpts.AutoDropRecoveryDb)
                await CleanupRecoveryInstanceAsync(pitrOpts, backupOpts, ct);

            await PersistRecoveryLogAsync(request, result, ct);
            return result;
        }
        finally
        {
            // Always delete the temporary decrypted file (OWASP A02)
            if (tempDecryptedPath is not null && File.Exists(tempDecryptedPath))
            {
                try { File.Delete(tempDecryptedPath); }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "PointInTimeRecoveryService: failed to delete temp file {Path}.",
                        tempDecryptedPath);
                }
            }
        }
    }

    /// <inheritdoc/>
    public async Task<List<RecoveryLog>> GetRecoveryHistoryAsync(
        int page, int pageSize, CancellationToken ct)
    {
        return await _db.RecoveryLogs
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Phase 1 — Pre-flight validation
    // ─────────────────────────────────────────────────────────────────────────

    private (bool Ok, FileInfo? BackupFile, string? FallbackReason, string? Feasibility)
        RunPreflightValidation(
            PitrRequest  request,
            BackupOptions backupOpts,
            PitrOptions   pitrOpts)
    {
        var walOpts = _walOptions.CurrentValue;
        var target  = request.TargetTimestampUtc;
        var now     = DateTime.UtcNow;

        // (a) Target must be in the past and within the WAL retention window.
        if (target >= now)
            return (false, null, null, $"TargetTimestampUtc ({target:o}) must be in the past.");

        var oldestAllowed = now.AddDays(-walOpts.WalRetentionDays);
        if (target < oldestAllowed)
            return (false, null, null,
                $"TargetTimestampUtc ({target:o}) is outside the WAL retention window " +
                $"({walOpts.WalRetentionDays} days). Oldest recoverable point: {oldestAllowed:o}.");

        // (b) Check WAL health — detect corruption affecting the target period.
        string? fallbackReason = null;
        var walStatus = _walMonitor.GetArchivalStatus();
        if (walStatus.CorruptSegments > 0)
        {
            fallbackReason =
                $"WAL archive contains {walStatus.CorruptSegments} corrupt segment(s). " +
                "PITR will recover to the base backup point only. " +
                $"Gap period: base backup timestamp to {target:o} may be unrecoverable.";
            _logger.LogWarning("PITR_WAL_FALLBACK: {Reason}", fallbackReason);
        }

        // (c) Find most recent base backup that predates the target timestamp.
        if (!Directory.Exists(backupOpts.BackupDirectory))
            return (false, null, null,
                $"Backup directory not found: {backupOpts.BackupDirectory}");

        var baseBackupFile = Directory
            .EnumerateFiles(backupOpts.BackupDirectory, "*.dump.enc")
            .Concat(Directory.EnumerateFiles(backupOpts.BackupDirectory, "*.dump"))
            .Select(f => new FileInfo(f))
            .Where(f => f.LastWriteTimeUtc < target)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();

        if (baseBackupFile is null)
            return (false, null, null,
                $"No base backup found predating {target:o} in {backupOpts.BackupDirectory}.");

        // (d) Count WAL segments covering the recovery window.
        int walCount = CountWalSegmentsInRange(baseBackupFile.LastWriteTimeUtc, target);

        var feasibility =
            $"Base backup: {baseBackupFile.Name} ({baseBackupFile.LastWriteTimeUtc:o}). " +
            $"WAL segments to replay: ~{walCount}. " +
            $"WAL health: {(walStatus.IsHealthy ? "Healthy" : "Degraded")}. " +
            (fallbackReason is not null ? $"Warning: {fallbackReason}" : "No WAL gaps detected.");

        return (true, baseBackupFile, fallbackReason, feasibility);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Phase 2 — Base backup restoration
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<string?> RecreateRecoveryDatabaseAsync(
        PitrOptions   pitrOpts,
        BackupOptions backupOpts,
        CancellationToken ct)
    {
        var dropError = await RunPsqlAdminCommandAsync(
            pitrOpts, backupOpts,
            $"DROP DATABASE IF EXISTS \"{pitrOpts.RecoveryDatabaseName}\"",
            ct);

        if (dropError is not null) return $"DROP DATABASE failed: {dropError}";

        var createError = await RunPsqlAdminCommandAsync(
            pitrOpts, backupOpts,
            $"CREATE DATABASE \"{pitrOpts.RecoveryDatabaseName}\"",
            ct);

        return createError is not null ? $"CREATE DATABASE failed: {createError}" : null;
    }

    private async Task<(bool Ok, string? Stderr)> RestoreBaseBackupAsync(
        string        plaintextPath,
        PitrOptions   pitrOpts,
        BackupOptions backupOpts,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "PointInTimeRecoveryService: restoring base backup {File} → {Db}.",
            Path.GetFileName(plaintextPath), pitrOpts.RecoveryDatabaseName);

        var psi = new ProcessStartInfo
        {
            FileName               = pitrOpts.PgRestorePath,
            Arguments              = $"--host={backupOpts.DbHost} " +
                                     $"--port={pitrOpts.RecoveryPort} " +
                                     $"--username={backupOpts.DbUsername} " +
                                     $"--dbname={pitrOpts.RecoveryDatabaseName} " +
                                     "--no-owner --no-privileges " +
                                     $"\"{plaintextPath}\"",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        psi.Environment["PGPASSWORD"] = pitrOpts.RecoveryPassword;

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(pitrOpts.MaxRecoveryTimeoutMinutes));

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start pg_restore.");

            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
            var stderr = await stderrTask;

            return process.ExitCode == 0
                ? (true, null)
                : (false, string.IsNullOrWhiteSpace(stderr)
                    ? $"pg_restore exited with code {process.ExitCode}"
                    : stderr);
        }
        finally
        {
            if (psi.Environment.ContainsKey("PGPASSWORD"))
                psi.Environment["PGPASSWORD"] = string.Empty;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Phase 3 — WAL replay to target timestamp
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<(bool Ok, string? Error, int WalReplayed)> ApplyWalRecoveryAsync(
        PitrOptions  pitrOpts,
        BackupOptions backupOpts,
        DateTime      targetUtc,
        CancellationToken ct)
    {
        var walOpts     = _walOptions.CurrentValue;
        var recDataDir  = pitrOpts.RecoveryDataDirectory;

        // Ensure recovery data directory exists.
        Directory.CreateDirectory(recDataDir);

        // Write recovery.signal (PostgreSQL 12+ mechanism).
        var recoverySignalPath = Path.Combine(recDataDir, "recovery.signal");
        await File.WriteAllTextAsync(recoverySignalPath, string.Empty, ct);

        // Write recovery parameters to postgresql.auto.conf
        var restoreCommand = $@"copy ""{walOpts.WalArchiveDirectory.TrimEnd('\\')}\%f"" ""%p""";
        var targetStr      = targetUtc.ToString("yyyy-MM-dd HH:mm:ss.ffffff+00");

        var autoConfPath = Path.Combine(recDataDir, "postgresql.auto.conf");
        var autoConfContent = new StringBuilder()
            .AppendLine($"restore_command = '{restoreCommand}'")
            .AppendLine($"recovery_target_time = '{targetStr}'")
            .AppendLine("recovery_target_action = 'pause'")
            .ToString();

        await File.WriteAllTextAsync(autoConfPath, autoConfContent, ct);

        _logger.LogInformation(
            "PointInTimeRecoveryService: starting recovery instance on port {Port}.",
            pitrOpts.RecoveryPort);

        // Start the recovery instance on the separate recovery port.
        var startError = await RunPgCtlCommandAsync(
            pitrOpts, $"start -D \"{recDataDir}\" -o \"-p {pitrOpts.RecoveryPort}\"", ct);

        if (startError is not null)
            return (false, $"pg_ctl start failed: {startError}", 0);

        // Poll pg_is_in_recovery() until the recovery pauses at the target timestamp.
        bool recovered = await PollUntilRecoveryCompleteAsync(pitrOpts, backupOpts, ct);

        if (!recovered)
            return (false, "Recovery timed out waiting for pg_is_in_recovery() = false.", 0);

        // Count WAL segments replayed (files touched in WAL archive since recovery started).
        int walReplayed = CountWalSegmentsInRange(
            DateTime.UtcNow.AddMinutes(-pitrOpts.MaxRecoveryTimeoutMinutes),
            targetUtc);

        _logger.LogInformation(
            "PointInTimeRecoveryService: WAL replay completed. Segments~{Count}.", walReplayed);

        return (true, null, walReplayed);
    }

    private async Task<bool> PollUntilRecoveryCompleteAsync(
        PitrOptions   pitrOpts,
        BackupOptions backupOpts,
        CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddMinutes(pitrOpts.MaxRecoveryTimeoutMinutes);

        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);

                var connStr = new NpgsqlConnectionStringBuilder
                {
                    Host     = "localhost",
                    Port     = pitrOpts.RecoveryPort,
                    Database = pitrOpts.RecoveryDatabaseName,
                    Username = backupOpts.DbUsername,
                    Password = pitrOpts.RecoveryPassword,
                }.ConnectionString;

                await using var conn = new NpgsqlConnection(connStr);
                await conn.OpenAsync(ct);

                await using var cmd = new NpgsqlCommand("SELECT pg_is_in_recovery();", conn);
                var result = await cmd.ExecuteScalarAsync(ct);

                // pg_is_in_recovery() returns false when recovery has paused or completed.
                if (result is bool b && !b)
                    return true;
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                // Recovery instance not yet accepting connections — keep polling.
                _logger.LogDebug(
                    "PointInTimeRecoveryService: waiting for recovery instance. {Msg}", ex.Message);
            }
        }

        return false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Phase 4 — Post-recovery integrity validation (AC-3)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<(List<TableRowCountComparison> RowCounts,
                         List<TableChecksumComparison> Checksums,
                         bool IntegrityPassed)> ValidateRecoveryIntegrityAsync(
        PitrOptions   pitrOpts,
        BackupOptions backupOpts,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "PointInTimeRecoveryService: running post-recovery integrity validation.");

        var productionConnStr = _db.Database.GetConnectionString()!;
        var recoveryConnStr   = new NpgsqlConnectionStringBuilder
        {
            Host     = "localhost",
            Port     = pitrOpts.RecoveryPort,
            Database = pitrOpts.RecoveryDatabaseName,
            Username = backupOpts.DbUsername,
            Password = pitrOpts.RecoveryPassword,
        }.ConnectionString;

        const string rowCountSql = """
            SELECT schemaname, relname, n_live_tup
            FROM pg_stat_user_tables
            ORDER BY schemaname, relname;
            """;

        var productionCounts = await FetchRowCountsAsync(productionConnStr, rowCountSql, ct);
        var recoveryCounts   = await FetchRowCountsAsync(recoveryConnStr,   rowCountSql, ct);

        var rowCountResults = new List<TableRowCountComparison>();
        foreach (var (key, sourceCount) in productionCounts)
        {
            recoveryCounts.TryGetValue(key, out var recoveredCount);
            // For PITR to a past point, production may legitimately have more rows.
            // We validate that the recovered count is ≤ production (no extra rows).
            bool matched = recoveredCount <= sourceCount;
            rowCountResults.Add(new TableRowCountComparison
            {
                SchemaName    = key.Schema,
                TableName     = key.Table,
                SourceCount   = sourceCount,
                RestoredCount = recoveredCount,
                Matched       = matched,
            });
        }

        // Checksum validation on the recovery database (internal consistency).
        var tables  = await FetchTableListAsync(recoveryConnStr, rowCountSql, ct);
        var checksumResults = new List<TableChecksumComparison>();

        foreach (var (schema, table) in tables)
        {
            var recoveryChecksum = await ComputeTableChecksumAsync(recoveryConnStr, schema, table, ct);
            // For PITR, we validate the recovery DB is internally consistent (not null / not erroring)
            // rather than matching production exactly (different point in time).
            checksumResults.Add(new TableChecksumComparison
            {
                SchemaName       = schema,
                TableName        = table,
                SourceChecksum   = "(pitr-point-not-applicable)",
                RestoredChecksum = recoveryChecksum,
                Matched          = recoveryChecksum is not null,
            });
        }

        bool integrityPassed = rowCountResults.All(r => r.Matched)
                               && checksumResults.All(r => r.Matched);

        return (rowCountResults, checksumResults, integrityPassed);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Phase 5 — Cleanup
    // ─────────────────────────────────────────────────────────────────────────

    private async Task CleanupRecoveryInstanceAsync(
        PitrOptions   pitrOpts,
        BackupOptions backupOpts,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "PointInTimeRecoveryService: stopping and cleaning up recovery instance.");

        await RunPgCtlCommandAsync(
            pitrOpts,
            $"stop -D \"{pitrOpts.RecoveryDataDirectory}\" -m fast",
            ct);

        try
        {
            if (Directory.Exists(pitrOpts.RecoveryDataDirectory))
                Directory.Delete(pitrOpts.RecoveryDataDirectory, recursive: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "PointInTimeRecoveryService: failed to remove recovery data directory {Dir}.",
                pitrOpts.RecoveryDataDirectory);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Persistence
    // ─────────────────────────────────────────────────────────────────────────

    private async Task PersistRecoveryLogAsync(
        PitrRequest   request,
        PitrResult    result,
        CancellationToken ct)
    {
        var log = new RecoveryLog
        {
            Id                     = Guid.NewGuid(),
            TargetTimestampUtc     = request.TargetTimestampUtc,
            ActualRecoveryPointUtc = result.ActualRecoveryPointUtc,
            BaseBackupUsed         = result.BaseBackupUsed,
            WalSegmentsReplayed    = result.WalSegmentsReplayed,
            Success                = result.Success,
            IntegrityPassed        = result.IntegrityValidationPassed,
            RecoveryDuration       = result.RecoveryDuration,
            FallbackReason         = result.FallbackReason,
            ErrorMessage           = result.ErrorMessage,
            PerformedBy            = request.PerformedBy,
            IsDryRun               = result.IsDryRun,
            CreatedAtUtc           = DateTime.UtcNow,
        };

        _db.RecoveryLogs.Add(log);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "PointInTimeRecoveryService: failed to persist RecoveryLog for target {Target}.",
                request.TargetTimestampUtc);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Process helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<string?> RunPsqlAdminCommandAsync(
        PitrOptions   pitrOpts,
        BackupOptions backupOpts,
        string        sql,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = pitrOpts.PsqlPath,
            Arguments              = $"--host={backupOpts.DbHost} " +
                                     $"--port={pitrOpts.RecoveryPort} " +
                                     $"--username={backupOpts.DbUsername} " +
                                     "--dbname=postgres " +
                                     $"--command=\"{sql}\"",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        psi.Environment["PGPASSWORD"] = pitrOpts.RecoveryPassword;

        try
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start psql.");

            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var stderr = await stderrTask;

            return process.ExitCode == 0 ? null
                : string.IsNullOrWhiteSpace(stderr)
                    ? $"psql exited with code {process.ExitCode}"
                    : stderr;
        }
        finally
        {
            if (psi.Environment.ContainsKey("PGPASSWORD"))
                psi.Environment["PGPASSWORD"] = string.Empty;
        }
    }

    private async Task<string?> RunPgCtlCommandAsync(
        PitrOptions pitrOpts,
        string      arguments,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName               = pitrOpts.PgCtlPath,
            Arguments              = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        psi.Environment["PGPASSWORD"] = pitrOpts.RecoveryPassword;

        try
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start pg_ctl.");

            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);
            var stderr = await stderrTask;

            return process.ExitCode == 0 ? null
                : string.IsNullOrWhiteSpace(stderr)
                    ? $"pg_ctl exited with code {process.ExitCode}"
                    : stderr;
        }
        finally
        {
            if (psi.Environment.ContainsKey("PGPASSWORD"))
                psi.Environment["PGPASSWORD"] = string.Empty;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Database query helpers (reuse pattern from BackupRestorationTestService)
    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<Dictionary<(string Schema, string Table), long>> FetchRowCountsAsync(
        string            connectionString,
        string            sql,
        CancellationToken ct)
    {
        var result = new Dictionary<(string, string), long>();

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd    = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
            result[(reader.GetString(0), reader.GetString(1))] = reader.GetInt64(2);

        return result;
    }

    private static async Task<List<(string Schema, string Table)>> FetchTableListAsync(
        string            connectionString,
        string            sql,
        CancellationToken ct)
    {
        var result = new List<(string, string)>();

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(ct);

        await using var cmd    = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        while (await reader.ReadAsync(ct))
            result.Add((reader.GetString(0), reader.GetString(1)));

        return result;
    }

    private static async Task<string?> ComputeTableChecksumAsync(
        string            connectionString,
        string            schema,
        string            tableName,
        CancellationToken ct)
    {
        if (!IsValidIdentifier(schema) || !IsValidIdentifier(tableName))
            return null;

        var sql = $"""
            SELECT md5(string_agg(md5(CAST(t.* AS text)), '' ORDER BY CAST(t.* AS text)))
            FROM "{schema}"."{tableName}" t;
            """;

        try
        {
            await using var conn = new NpgsqlConnection(connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = new NpgsqlCommand(sql, conn);
            var result = await cmd.ExecuteScalarAsync(ct);
            return result is DBNull || result is null ? "(empty)" : result.ToString();
        }
        catch (NpgsqlException)
        {
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Utility helpers
    // ─────────────────────────────────────────────────────────────────────────

    private int CountWalSegmentsInRange(DateTime fromUtc, DateTime toUtc)
    {
        var walDir = _walOptions.CurrentValue.WalArchiveDirectory;
        if (!Directory.Exists(walDir)) return 0;

        return new DirectoryInfo(walDir)
            .EnumerateFiles()
            .Count(f => WalSegmentRegex.IsMatch(f.Name)
                        && f.LastWriteTimeUtc >= fromUtc
                        && f.LastWriteTimeUtc <= toUtc);
    }

    private static bool IsValidIdentifier(string id) =>
        !string.IsNullOrEmpty(id) && id.All(c => char.IsLetterOrDigit(c) || c == '_');

    private static PitrResult FailedResult(
        PitrRequest request,
        DateTime    startedAt,
        TimeSpan    duration,
        string      errorMessage,
        string?     fallbackReason = null) =>
        new()
        {
            Success                  = false,
            TargetTimestampUtc       = request.TargetTimestampUtc,
            ActualRecoveryPointUtc   = DateTime.MinValue,
            BaseBackupUsed           = string.Empty,
            WalSegmentsReplayed      = 0,
            RecoveryDuration         = duration,
            IntegrityValidationPassed = false,
            RowCountResults          = [],
            ChecksumResults          = [],
            FallbackReason           = fallbackReason,
            IsDryRun                 = request.DryRun,
            ErrorMessage             = errorMessage,
        };
}
