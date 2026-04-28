using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.Service.Backup.Models;

namespace UPACIP.Service.Backup;

/// <summary>
/// Nightly BackgroundService that orchestrates automated PostgreSQL backups at
/// <see cref="BackupOptions.ScheduleLocalTime"/> (default 2 AM local time) using
/// <c>pg_dump</c> via <see cref="IBackupExecutor"/> (US_088, DR-022).
///
/// Per-cycle flow:
/// <list type="number">
///   <item>Compute the next scheduled local execution time.</item>
///   <item>Wait via <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</item>
///   <item>Check disk space on the backup volume (edge case 1 — 80% threshold).</item>
///   <item>Execute <c>pg_dump</c> via <see cref="IBackupExecutor"/>.</item>
///   <item>
///     On success: persist <see cref="BackupLog"/> with <c>Status = "Completed"</c>
///     and emit <c>BACKUP_COMPLETED</c> structured log (AC-3).
///   </item>
///   <item>
///     On failure (AC-4): wait <see cref="BackupOptions.RetryDelayMinutes"/> minutes,
///     retry once.  If the retry also fails, emit <c>BACKUP_CRITICAL_FAILURE</c> alert.
///   </item>
/// </list>
///
/// <c>ApplicationDbContext</c> is resolved per cycle from <see cref="IServiceScopeFactory"/>
/// following the standard .NET BackgroundService scoped-service pattern.
/// </summary>
public sealed class DatabaseBackupService : BackgroundService
{
    private readonly IServiceScopeFactory                       _scopeFactory;
    private readonly IBackupExecutor                            _backupExecutor;
    private readonly IBackupRetentionService                    _retentionService;
    private readonly IBackupEncryptionService                   _encryptionService;
    private readonly IBackupReplicationService                  _replicationService;
    private readonly IOptionsMonitor<BackupOptions>             _options;
    private readonly IOptionsMonitor<EncryptionOptions>         _encryptionOptions;
    private readonly IOptionsMonitor<ReplicationOptions>        _replicationOptions;
    private readonly ILogger<DatabaseBackupService>             _logger;

    public DatabaseBackupService(
        IServiceScopeFactory                       scopeFactory,
        IBackupExecutor                            backupExecutor,
        IBackupRetentionService                    retentionService,
        IBackupEncryptionService                   encryptionService,
        IBackupReplicationService                  replicationService,
        IOptionsMonitor<BackupOptions>             options,
        IOptionsMonitor<EncryptionOptions>         encryptionOptions,
        IOptionsMonitor<ReplicationOptions>        replicationOptions,
        ILogger<DatabaseBackupService>             logger)
    {
        _scopeFactory       = scopeFactory;
        _backupExecutor     = backupExecutor;
        _retentionService   = retentionService;
        _encryptionService  = encryptionService;
        _replicationService = replicationService;
        _options            = options;
        _encryptionOptions  = encryptionOptions;
        _replicationOptions = replicationOptions;
        _logger             = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BackgroundService loop
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "DatabaseBackupService started. ScheduleLocalTime={Schedule}",
            _options.CurrentValue.ScheduleLocalTime);

        while (!stoppingToken.IsCancellationRequested)
        {
            var opts       = _options.CurrentValue;
            var nextRunLocal = ComputeNextRunLocal(opts.ScheduleLocalTime);
            var delay        = nextRunLocal - DateTime.Now;

            if (delay > TimeSpan.Zero)
            {
                _logger.LogDebug(
                    "DatabaseBackupService: next backup scheduled at {NextRun} local " +
                    "(delay {DelayMinutes:F0} minutes).",
                    nextRunLocal, delay.TotalMinutes);

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }

            // Re-read options after the wait — the schedule may have changed.
            opts = _options.CurrentValue;

            try
            {
                await RunBackupCycleAsync(opts, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex,
                    "DatabaseBackupService: unhandled exception during backup cycle. " +
                    "Will retry at next scheduled time.");
            }

            // 30-second guard to prevent tight spin if clock-skew causes ComputeNextRunLocal
            // to return a time already in the past.
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken)
                .ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnCanceled);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Backup cycle
    // ─────────────────────────────────────────────────────────────────────────

    private async Task RunBackupCycleAsync(BackupOptions opts, CancellationToken ct)
    {
        _logger.LogInformation("DatabaseBackupService: starting backup cycle.");

        // ── Step 1: Disk space pre-check (edge case 1) ────────────────────────
        bool diskOk = await CheckDiskSpaceAsync(opts, ct);
        if (!diskOk)
        {
            // CheckDiskSpaceAsync already persisted a "Skipped" BackupLog and emitted the alert.
            return;
        }

        // ── Step 2: Initial backup attempt ───────────────────────────────────
        var firstResult = await _backupExecutor.ExecuteBackupAsync(isRetry: false, ct: ct);

        if (firstResult.Success)
        {
            firstResult = await ApplyEncryptionAsync(firstResult, ct);
            await PersistBackupLogAsync(firstResult, DetermineStatus(firstResult), ct);
            EmitCompletedLog(firstResult);
            await RunReplicationAsync(firstResult, ct);
            await RunRetentionCleanupAsync(firstResult.FileName, ct);
            return;
        }

        // ── Step 3: First attempt failed — single retry (AC-4) ───────────────
        _logger.LogWarning(
            "BACKUP_FAILED: Attempt=1, FileName={FileName}, Error={Error}. " +
            "Retrying in {Delay} minutes.",
            firstResult.FileName, firstResult.ErrorMessage, opts.RetryDelayMinutes);

        try
        {
            await Task.Delay(TimeSpan.FromMinutes(opts.RetryDelayMinutes), ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning(
                "DatabaseBackupService: retry wait cancelled by application shutdown.");
            return;
        }

        var retryResult = await _backupExecutor.ExecuteBackupAsync(isRetry: true, ct: ct);

        if (retryResult.Success)
        {
            retryResult = await ApplyEncryptionAsync(retryResult, ct);
            await PersistBackupLogAsync(retryResult, DetermineStatus(retryResult), ct);
            EmitCompletedLog(retryResult);
            await RunReplicationAsync(retryResult, ct);
            await RunRetentionCleanupAsync(retryResult.FileName, ct);
            return;
        }

        // ── Step 4: Both attempts failed — critical alert (AC-4) ─────────────
        await PersistBackupLogAsync(retryResult, "Failed", ct);

        _logger.LogError(
            "BACKUP_CRITICAL_FAILURE: Both attempts failed. " +
            "Attempt1Error={FirstError}, RetryError={RetryError}. " +
            "Administrator action required.",
            firstResult.ErrorMessage,
            retryResult.ErrorMessage);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Encryption post-processing (US_089, AC-1, DR-025)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Encrypts a successful backup result if encryption is enabled.
    /// On success: returns a new <see cref="BackupResult"/> with encrypted file metadata
    /// replacing the plaintext values so downstream consumers (BackupLog, RetentionService)
    /// operate on the encrypted artifact.
    /// On failure: logs a warning, preserves the plaintext file, and returns the original
    /// result with <c>Status = "CompletedUnencrypted"</c> so the backup cycle is not failed.
    /// </summary>
    private async Task<BackupResult> ApplyEncryptionAsync(
        BackupResult      result,
        CancellationToken ct)
    {
        if (!_encryptionOptions.CurrentValue.Enabled)
            return result;

        try
        {
            var encResult = await _encryptionService.EncryptBackupAsync(result.FilePath, ct);

            // Replace file metadata so BackupLog and RetentionService see the encrypted file.
            return result with
            {
                FilePath               = encResult.EncryptedFilePath,
                FileSizeBytes          = encResult.EncryptedFileSizeBytes,
                Checksum               = encResult.Checksum,
                FileName               = Path.GetFileName(encResult.EncryptedFilePath),
                EncryptedFilePath      = encResult.EncryptedFilePath,
                EncryptedFileSizeBytes = encResult.EncryptedFileSizeBytes,
                EncryptedChecksum      = encResult.Checksum,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "DatabaseBackupService: backup encryption failed for {File}. " +
                "Plaintext backup retained. Status will be CompletedUnencrypted.",
                result.FileName);

            // Return original result — plaintext .dump file is intact.
            return result;
        }
    }

    /// <summary>
    /// Determines the BackupLog status string based on whether encryption was applied.
    /// </summary>
    private string DetermineStatus(BackupResult result)
    {
        if (!_encryptionOptions.CurrentValue.Enabled)
            return "Completed";

        return result.EncryptedFilePath is not null
            ? "Completed"
            : "CompletedUnencrypted";
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Geographic replication (AC-2, DR-024)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Replicates the encrypted backup file to the configured remote UNC share.
    /// Only runs when encryption succeeded (i.e., <c>FilePath</c> ends in <c>.dump.enc</c>)
    /// so that plaintext backups are never sent off-server.
    /// Replication failure is non-fatal — the backup cycle is still considered successful.
    /// </summary>
    private async Task RunReplicationAsync(BackupResult result, CancellationToken ct)
    {
        var replOpts = _replicationOptions.CurrentValue;
        if (!replOpts.Enabled)
            return;

        // Only replicate the encrypted artifact — never the plaintext .dump.
        if (!result.FilePath.EndsWith(".dump.enc", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "DatabaseBackupService: skipping replication of non-encrypted backup {File}. " +
                "Encryption must succeed before replication.",
                result.FileName);
            return;
        }

        try
        {
            var replResult = await _replicationService.ReplicateBackupAsync(result.FilePath, ct);

            var status = replResult.Success ? "Replicated" : "ReplicationFailed";
            await PersistReplicationLogAsync(result.FileName, replResult, ct);

            if (!replResult.Success)
            {
                _logger.LogError(
                    "BACKUP_REPLICATION_FAILED: Backup preserved locally at {Path}. Error={Error}",
                    result.FilePath,
                    replResult.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            // Log and continue — replication failure must never fail the backup cycle.
            _logger.LogError(ex,
                "DatabaseBackupService: unhandled exception during replication of {File}.",
                result.FileName);
        }
    }

    private async Task PersistReplicationLogAsync(
        string             backupFileName,
        ReplicationResult  replResult,
        CancellationToken  ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db          = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var log = new BackupLog
        {
            Id            = Guid.NewGuid(),
            FileName      = backupFileName,
            FileSizeBytes = replResult.FileSizeBytes,
            Duration      = replResult.TransferDuration,
            Status        = replResult.Success ? "Replicated" : "ReplicationFailed",
            Checksum      = string.Empty,   // checksum already validated during transfer
            ErrorMessage  = replResult.ErrorMessage,
            WasRetry      = replResult.AttemptsUsed > 1,
            CreatedAtUtc  = DateTime.UtcNow,
        };

        db.BackupLogs.Add(log);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DatabaseBackupService: failed to persist ReplicationLog for {FileName}.",
                backupFileName);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Retention cleanup (AC-2, DR-023)    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the tiered backup retention cleanup after a successful backup.
    /// A failure here does NOT fail the backup cycle — the backup itself succeeded.
    /// </summary>
    private async Task RunRetentionCleanupAsync(string backupFileName, CancellationToken ct)
    {
        try
        {
            var retentionResult = await _retentionService.ApplyRetentionPolicyAsync(ct);

            _logger.LogInformation(
                "BACKUP_CYCLE_COMPLETE: BackupFile={FileName}, " +
                "RetentionDeleted={Deleted}, FreedBytes={FreedBytes}",
                backupFileName,
                retentionResult.DeletedCount,
                retentionResult.FreedBytes);
        }
        catch (Exception ex)
        {
            // Log and continue — retention cleanup failure must not surface as an
            // unhandled exception that invalidates a successful backup.
            _logger.LogError(ex,
                "DatabaseBackupService: retention cleanup failed after successful backup {File}. " +
                "Will retry on next backup cycle.",
                backupFileName);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Disk space pre-check (edge case 1)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<bool> CheckDiskSpaceAsync(BackupOptions opts, CancellationToken ct)
    {
        try
        {
            var root = Path.GetPathRoot(opts.BackupDirectory);
            if (string.IsNullOrWhiteSpace(root))
            {
                _logger.LogWarning(
                    "DatabaseBackupService: unable to determine drive root for " +
                    "BackupDirectory={Dir}. Skipping disk check.",
                    opts.BackupDirectory);
                return true; // proceed — cannot determine drive
            }

            var drive        = new DriveInfo(root);
            double usagePct  = (1.0 - drive.AvailableFreeSpace / (double)drive.TotalSize) * 100.0;

            if (usagePct >= opts.DiskSpaceThresholdPercent)
            {
                _logger.LogError(
                    "BACKUP_STORAGE_CRITICAL: Disk usage {UsagePercent:F1}% exceeds threshold " +
                    "{Threshold:F1}%. Drive={Drive}. Backup skipped.",
                    usagePct, opts.DiskSpaceThresholdPercent, drive.Name);

                await PersistSkippedLogAsync(
                    $"Backup skipped — disk at {usagePct:F1}% (threshold {opts.DiskSpaceThresholdPercent:F1}%)",
                    ct);

                return false;
            }

            if (usagePct >= opts.DiskSpaceWarningPercent)
            {
                _logger.LogWarning(
                    "BACKUP_STORAGE_WARNING: Disk usage {UsagePercent:F1}% is approaching " +
                    "threshold {Threshold:F1}%. Drive={Drive}.",
                    usagePct, opts.DiskSpaceThresholdPercent, drive.Name);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "DatabaseBackupService: disk space check failed. Proceeding with backup attempt.");
            return true; // fail-open — prefer attempting the backup over silently skipping
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Persistence helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task PersistBackupLogAsync(
        BackupResult      result,
        string            status,
        CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db          = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var log = new BackupLog
        {
            Id            = Guid.NewGuid(),
            FileName      = result.FileName,
            FileSizeBytes = result.FileSizeBytes,
            Duration      = result.Duration,
            Status        = status,
            Checksum      = result.Checksum,
            ErrorMessage  = result.ErrorMessage,
            WasRetry      = result.IsRetry,
            CreatedAtUtc  = result.CompletedAtUtc,
        };

        db.BackupLogs.Add(log);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Log but do not throw — backup-log persistence failure must not surface
            // as an unhandled exception that prevents the next backup cycle.
            _logger.LogError(ex,
                "DatabaseBackupService: failed to persist BackupLog for {FileName}.",
                result.FileName);
        }
    }

    private async Task PersistSkippedLogAsync(string reason, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db          = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var log = new BackupLog
        {
            Id            = Guid.NewGuid(),
            FileName      = string.Empty,
            FileSizeBytes = 0,
            Duration      = TimeSpan.Zero,
            Status        = "Skipped",
            Checksum      = string.Empty,
            ErrorMessage  = reason,
            WasRetry      = false,
            CreatedAtUtc  = DateTime.UtcNow,
        };

        db.BackupLogs.Add(log);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DatabaseBackupService: failed to persist skipped BackupLog entry.");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Logging
    // ─────────────────────────────────────────────────────────────────────────

    private void EmitCompletedLog(BackupResult result)
    {
        _logger.LogInformation(
            "BACKUP_COMPLETED: FileName={FileName}, Size={SizeBytes} bytes, " +
            "Duration={Duration}, Checksum={Checksum}, WasRetry={WasRetry}",
            result.FileName,
            result.FileSizeBytes,
            result.Duration,
            result.Checksum,
            result.IsRetry);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Schedule helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the next local execution time from the configured <c>HH:mm</c> string.
    /// If the target time has already passed for today in local time, schedules for tomorrow.
    /// </summary>
    private static DateTime ComputeNextRunLocal(string scheduleLocal)
    {
        if (!TimeSpan.TryParseExact(scheduleLocal, @"hh\:mm", null, out var timeOfDay))
            timeOfDay = new TimeSpan(2, 0, 0); // fallback to 02:00

        var localNow    = DateTime.Now;
        var localTarget = localNow.Date.Add(timeOfDay);

        if (localTarget <= localNow)
            localTarget = localTarget.AddDays(1);

        return localTarget;
    }
}
