using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.Service.Backup.Models;

namespace UPACIP.Service.Backup;

// ─────────────────────────────────────────────────────────────────────────────
// Result DTO
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Summary of a single retention-cleanup pass (US_088, AC-2, DR-023).
/// Returned by <see cref="IBackupRetentionService.ApplyRetentionPolicyAsync"/>.
/// </summary>
public sealed record RetentionCleanupResult
{
    /// <summary>Total backup files found in the backup directory.</summary>
    public int TotalScanned { get; init; }

    /// <summary>Number of expired files deleted in this pass.</summary>
    public int DeletedCount { get; init; }

    /// <summary>Total bytes freed by deleting expired files.</summary>
    public long FreedBytes { get; init; }

    /// <summary>Daily-tier files retained (not expired).</summary>
    public int RetainedDaily { get; init; }

    /// <summary>Weekly-tier files retained (not expired).</summary>
    public int RetainedWeekly { get; init; }

    /// <summary>Monthly-tier files retained (not expired).</summary>
    public int RetainedMonthly { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Interface
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Enforces the three-tier backup retention policy on files in the configured
/// backup directory (US_088, AC-2, DR-023).
/// </summary>
public interface IBackupRetentionService
{
    /// <summary>
    /// Scans the backup directory, classifies each file by tier, deletes expired files,
    /// and returns a <see cref="RetentionCleanupResult"/> summary.
    /// </summary>
    Task<RetentionCleanupResult> ApplyRetentionPolicyAsync(CancellationToken ct = default);
}

// ─────────────────────────────────────────────────────────────────────────────
// Implementation
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Singleton implementation of <see cref="IBackupRetentionService"/>.
///
/// Tier classification rules (highest tier wins):
/// <list type="bullet">
///   <item><b>Monthly</b>: backup day-of-month == <see cref="BackupRetentionOptions.MonthlyBackupDayOfMonth"/> (default: 1). Retained 365 days.</item>
///   <item><b>Weekly</b>: backup day-of-week == <see cref="BackupRetentionOptions.WeeklyBackupDay"/> (default: Sunday). Retained 90 days.</item>
///   <item><b>Daily</b>: all other backups. Retained 30 days.</item>
/// </list>
///
/// Edge case — Sunday the 1st: classified as Monthly (highest retention wins → 365 days).
///
/// Edge case — missing backups: no gap-filling; retention applies to whatever files exist.
///
/// Edge case — unrecognised filenames: files not matching <c>upacip_backup_yyyyMMdd_HHmmss.dump</c>
/// are skipped with a warning and never deleted by this service.
///
/// <c>ApplicationDbContext</c> is resolved per call from <see cref="IServiceScopeFactory"/> so
/// this Singleton can safely consume a Scoped EF Core context.
/// </summary>
public sealed class BackupRetentionService : IBackupRetentionService
{
    // Matches both plaintext and encrypted backup files:
    //   upacip_backup_20260428_020000.dump
    //   upacip_backup_20260428_020000.dump.enc
    private static readonly Regex BackupFilePattern =
        new(@"^upacip_backup_(\d{8}_\d{6})\.dump(\.enc)?$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private const string TimestampFormat = "yyyyMMdd_HHmmss";

    private readonly IServiceScopeFactory                          _scopeFactory;
    private readonly IOptionsMonitor<BackupRetentionOptions>       _retentionOptions;
    private readonly IOptionsMonitor<BackupOptions>                _backupOptions;
    private readonly ILogger<BackupRetentionService>               _logger;

    public BackupRetentionService(
        IServiceScopeFactory                          scopeFactory,
        IOptionsMonitor<BackupRetentionOptions>       retentionOptions,
        IOptionsMonitor<BackupOptions>                backupOptions,
        ILogger<BackupRetentionService>               logger)
    {
        _scopeFactory     = scopeFactory;
        _retentionOptions = retentionOptions;
        _backupOptions    = backupOptions;
        _logger           = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public interface
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<RetentionCleanupResult> ApplyRetentionPolicyAsync(
        CancellationToken ct = default)
    {
        var retention = _retentionOptions.CurrentValue;
        var backup    = _backupOptions.CurrentValue;
        var now       = DateTime.UtcNow;

        _logger.LogInformation(
            "BackupRetentionService: starting retention scan in {Dir}. " +
            "Policy: Daily={DailyDays}d, Weekly={WeeklyDays}d, Monthly={MonthlyDays}d",
            backup.BackupDirectory,
            retention.DailyRetentionDays,
            retention.WeeklyRetentionDays,
            retention.MonthlyRetentionDays);

        // ── (a) Scan backup directory ─────────────────────────────────────────
        if (!Directory.Exists(backup.BackupDirectory))
        {
            _logger.LogWarning(
                "BackupRetentionService: backup directory does not exist: {Dir}. " +
                "Skipping retention scan.",
                backup.BackupDirectory);
            return new RetentionCleanupResult();
        }

        var files = Directory
            .EnumerateFiles(backup.BackupDirectory, "upacip_backup_*.dump",
                SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(backup.BackupDirectory, "upacip_backup_*.dump.enc",
                SearchOption.TopDirectoryOnly))
            .ToArray();

        // ── (b) Parse and classify each file ─────────────────────────────────
        var classified = new List<BackupFileInfo>(files.Length);

        foreach (var filePath in files)
        {
            var fileName = Path.GetFileName(filePath);
            var match    = BackupFilePattern.Match(fileName);

            if (!match.Success)
            {
                _logger.LogWarning(
                    "BackupRetentionService: file does not match expected naming pattern " +
                    "and will be skipped: {File}",
                    fileName);
                continue;
            }

            if (!DateTime.TryParseExact(
                    match.Groups[1].Value,
                    TimestampFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var backupTimestamp))
            {
                _logger.LogWarning(
                    "BackupRetentionService: unable to parse timestamp from filename {File}. " +
                    "Skipping.",
                    fileName);
                continue;
            }

            long sizeBytes = 0;
            try
            {
                sizeBytes = new FileInfo(filePath).Length;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "BackupRetentionService: unable to read file size for {File}. " +
                    "Proceeding with size=0.",
                    fileName);
            }

            // ── (c) Tier classification — highest applicable tier wins ─────────
            var tier = ClassifyTier(backupTimestamp, retention);

            // ── (d) Expiration check ───────────────────────────────────────────
            int ageDays    = (int)(now - backupTimestamp.ToUniversalTime()).TotalDays;
            int maxAgeDays = tier switch
            {
                BackupTier.Monthly => retention.MonthlyRetentionDays,
                BackupTier.Weekly  => retention.WeeklyRetentionDays,
                _                  => retention.DailyRetentionDays,
            };
            bool isExpired = ageDays > maxAgeDays;

            classified.Add(new BackupFileInfo
            {
                FileName        = fileName,
                FullPath        = filePath,
                BackupTimestamp = backupTimestamp,
                FileSizeBytes   = sizeBytes,
                Tier            = tier,
                AgeDays         = ageDays,
                IsExpired       = isExpired,
            });
        }

        // ── (e) Delete expired files ──────────────────────────────────────────
        int  deletedCount = 0;
        long freedBytes   = 0;

        foreach (var file in classified.Where(f => f.IsExpired))
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                File.Delete(file.FullPath);
                deletedCount++;
                freedBytes += file.FileSizeBytes;

                _logger.LogInformation(
                    "BACKUP_RETENTION_DELETE: File={FileName}, Tier={Tier}, " +
                    "Age={AgeDays}d, Size={SizeBytes}",
                    file.FileName, file.Tier, file.AgeDays, file.FileSizeBytes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "BackupRetentionService: failed to delete expired backup {File}. " +
                    "Will retry on next cycle.",
                    file.FileName);
            }
        }

        // ── Tally retained files ──────────────────────────────────────────────
        var retained = classified.Where(f => !f.IsExpired).ToList();
        int retainedDaily   = retained.Count(f => f.Tier == BackupTier.Daily);
        int retainedWeekly  = retained.Count(f => f.Tier == BackupTier.Weekly);
        int retainedMonthly = retained.Count(f => f.Tier == BackupTier.Monthly);

        var result = new RetentionCleanupResult
        {
            TotalScanned    = classified.Count,
            DeletedCount    = deletedCount,
            FreedBytes      = freedBytes,
            RetainedDaily   = retainedDaily,
            RetainedWeekly  = retainedWeekly,
            RetainedMonthly = retainedMonthly,
        };

        // ── (f) Summary logging ───────────────────────────────────────────────
        _logger.LogInformation(
            "BACKUP_RETENTION_COMPLETE: Scanned={TotalFiles}, Deleted={DeletedCount}, " +
            "FreedBytes={FreedBytes}, Retained={RetainedCount} " +
            "(Daily={DailyCount}, Weekly={WeeklyCount}, Monthly={MonthlyCount})",
            result.TotalScanned,
            result.DeletedCount,
            result.FreedBytes,
            retainedDaily + retainedWeekly + retainedMonthly,
            retainedDaily,
            retainedWeekly,
            retainedMonthly);

        // ── (g) Persist retention audit log to BackupLog ──────────────────────
        if (deletedCount > 0)
        {
            await PersistRetentionLogAsync(result, ct);
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Tier classification
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the highest applicable tier for a backup taken at <paramref name="backupTimestamp"/>.
    ///
    /// Priority order: Monthly > Weekly > Daily.
    ///
    /// A backup on Sunday January 1st qualifies for both Monthly and Weekly.
    /// Monthly is returned (highest retention wins, 365 days).
    /// </summary>
    private static BackupTier ClassifyTier(
        DateTime                 backupTimestamp,
        BackupRetentionOptions   opts)
    {
        if (backupTimestamp.Day == opts.MonthlyBackupDayOfMonth)
            return BackupTier.Monthly;

        if (backupTimestamp.DayOfWeek == opts.WeeklyBackupDay)
            return BackupTier.Weekly;

        return BackupTier.Daily;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Persistence
    // ─────────────────────────────────────────────────────────────────────────

    private async Task PersistRetentionLogAsync(
        RetentionCleanupResult result,
        CancellationToken      ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db          = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var log = new BackupLog
        {
            Id            = Guid.NewGuid(),
            FileName      = string.Empty,
            FileSizeBytes = 0,
            Duration      = TimeSpan.Zero,
            Status        = "RetentionCleanup",
            Checksum      = string.Empty,
            ErrorMessage  = $"Deleted={result.DeletedCount}, FreedBytes={result.FreedBytes}, " +
                            $"Retained(D/W/M)={result.RetainedDaily}/{result.RetainedWeekly}/{result.RetainedMonthly}",
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
                "BackupRetentionService: failed to persist retention audit log entry.");
        }
    }
}
