using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.Service.Backup.Models;

namespace UPACIP.Service.Backup;

/// <summary>
/// BackgroundService that ensures continuous WAL archival health for point-in-time recovery
/// (US_090, AC-1, AC-4, DR-027, NFR-024).
///
/// Responsibilities:
/// <list type="number">
///   <item>Force WAL segment completion every 15 minutes via <c>pg_switch_wal()</c> (AC-1).</item>
///   <item>Monitor the WAL archive directory every 5 minutes for stalled archiving, gaps, and corruption.</item>
///   <item>Validate WAL integrity via <c>pg_waldump</c> (edge case 1).</item>
///   <item>Clean up WAL segments older than the retention window once daily.</item>
/// </list>
///
/// Security notes (OWASP A02 / A09):
/// <list type="bullet">
///   <item>PostgreSQL credentials sourced from <see cref="BackupOptions"/> — password set on child process
///     environment only (<c>PGPASSWORD</c>), never in CLI arguments.</item>
///   <item>Passwords cleared from <c>ProcessStartInfo.Environment</c> in <c>finally</c> blocks.</item>
/// </list>
/// </summary>
public sealed class WalArchivalMonitoringService : BackgroundService
{
    // WAL segment filename format: TTTTTTTT00000000SSSSSSSS (24 hex chars)
    // T = 8-char timeline ID, S = 8-char segment number within each log file.
    private static readonly Regex WalSegmentRegex =
        new(@"^(?<timeline>[0-9A-Fa-f]{8})(?<log>[0-9A-Fa-f]{8})(?<seg>[0-9A-Fa-f]{8})$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IOptionsMonitor<WalArchivalOptions> _walOptions;
    private readonly IOptionsMonitor<BackupOptions>      _backupOptions;
    private readonly ILogger<WalArchivalMonitoringService> _logger;

    // Tracks the last time the daily retention cleanup ran (one cleanup per calendar day).
    private DateTime _lastRetentionCleanupDate = DateTime.MinValue;

    // Most recent archival status — exposed for health check integration.
    private volatile WalArchivalStatus _latestStatus = new()
    {
        IsHealthy              = true,
        CapturedAtUtc          = DateTime.UtcNow,
        MinutesSinceLastArchive = int.MaxValue,
    };

    public WalArchivalMonitoringService(
        IOptionsMonitor<WalArchivalOptions>    walOptions,
        IOptionsMonitor<BackupOptions>         backupOptions,
        ILogger<WalArchivalMonitoringService>  logger)
    {
        _walOptions    = walOptions;
        _backupOptions = backupOptions;
        _logger        = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BackgroundService loop
    // ─────────────────────────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "WalArchivalMonitoringService started. " +
            "SwitchInterval={SwitchMin}min, MonitorInterval={MonitorMin}min.",
            _walOptions.CurrentValue.SwitchWalIntervalMinutes,
            _walOptions.CurrentValue.MonitoringCheckIntervalMinutes);

        // Run the two loops concurrently — they share only the logger and volatile status field.
        var switchTask  = RunSwitchWalLoopAsync(stoppingToken);
        var monitorTask = RunMonitoringLoopAsync(stoppingToken);

        await Task.WhenAll(switchTask, monitorTask);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Health status accessor (used by health checks and task_002 PITR service)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the most recent WAL archival health snapshot.
    /// Thread-safe via <c>volatile</c> read.
    /// </summary>
    public WalArchivalStatus GetArchivalStatus() => _latestStatus;

    // ─────────────────────────────────────────────────────────────────────────
    // pg_switch_wal loop (every 15 minutes)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task RunSwitchWalLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var opts = _walOptions.CurrentValue;

            try
            {
                await Task.Delay(
                    TimeSpan.FromMinutes(opts.SwitchWalIntervalMinutes), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }

            await ExecuteSwitchWalAsync(opts, ct);
        }
    }

    /// <summary>
    /// Issues <c>SELECT pg_switch_wal();</c> to force the current WAL segment
    /// to be completed and handed to the archive_command (AC-1).
    /// Failure is non-fatal — logs a warning and continues to the next interval.
    /// </summary>
    private async Task ExecuteSwitchWalAsync(WalArchivalOptions opts, CancellationToken ct)
    {
        var backupOpts = _backupOptions.CurrentValue;

        var psi = new ProcessStartInfo
        {
            FileName               = opts.PsqlPath,
            Arguments              = $"--host={backupOpts.DbHost} " +
                                     $"--port={backupOpts.DbPort} " +
                                     $"--username={backupOpts.DbUsername} " +
                                     $"--dbname={backupOpts.DatabaseName} " +
                                     "--command=\"SELECT pg_switch_wal();\"",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        // Credential injected only into the child process environment (OWASP A02).
        psi.Environment["PGPASSWORD"] = backupOpts.DbPassword;

        try
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start psql for pg_switch_wal.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode == 0)
            {
                _logger.LogDebug(
                    "WAL_SWITCH_EXECUTED: Result={Result}",
                    stdout.Trim());
            }
            else
            {
                _logger.LogWarning(
                    "WalArchivalMonitoringService: pg_switch_wal failed (exit {Code}). " +
                    "Stderr={Stderr}. Will retry on next interval.",
                    process.ExitCode, stderr.Trim());
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "WalArchivalMonitoringService: exception during pg_switch_wal. " +
                "Will retry on next interval.");
        }
        finally
        {
            if (psi.Environment.ContainsKey("PGPASSWORD"))
                psi.Environment["PGPASSWORD"] = string.Empty;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Monitoring loop (every 5 minutes)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task RunMonitoringLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var opts = _walOptions.CurrentValue;

            try
            {
                await RunMonitoringCycleAsync(opts, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError(ex,
                    "WalArchivalMonitoringService: unhandled exception during monitoring cycle.");
            }

            try
            {
                await Task.Delay(
                    TimeSpan.FromMinutes(opts.MonitoringCheckIntervalMinutes), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task RunMonitoringCycleAsync(WalArchivalOptions opts, CancellationToken ct)
    {
        // Ensure archive directory exists before scanning.
        if (!Directory.Exists(opts.WalArchiveDirectory))
        {
            _logger.LogWarning(
                "WalArchivalMonitoringService: archive directory does not exist: {Dir}. " +
                "Has Setup-WalArchiving.ps1 been run?",
                opts.WalArchiveDirectory);

            _latestStatus = new WalArchivalStatus
            {
                IsHealthy               = false,
                CapturedAtUtc           = DateTime.UtcNow,
                MinutesSinceLastArchive = int.MaxValue,
            };
            return;
        }

        // ── (a) Scan archive directory ────────────────────────────────────────
        var (files, totalBytes) = ScanArchiveDirectory(opts.WalArchiveDirectory);
        var walFiles = files
            .Where(f => WalSegmentRegex.IsMatch(f.Name))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .ToList();

        FileInfo? latestFile  = walFiles.FirstOrDefault();
        DateTime? lastArchivedAt = latestFile?.LastWriteTimeUtc;
        int minutesSinceLast = lastArchivedAt.HasValue
            ? (int)(DateTime.UtcNow - lastArchivedAt.Value).TotalMinutes
            : int.MaxValue;

        // ── (b) Stall detection ───────────────────────────────────────────────
        if (minutesSinceLast > opts.MaxWalArchivalDelayMinutes)
        {
            _logger.LogError(
                "WAL_ARCHIVAL_STALLED: LastArchivedAt={LastModified}, " +
                "MinutesSinceLastArchive={Minutes}, Threshold={Threshold}min",
                lastArchivedAt?.ToString("o") ?? "never",
                minutesSinceLast,
                opts.MaxWalArchivalDelayMinutes);
        }

        // ── (c) Gap detection ─────────────────────────────────────────────────
        int detectedGaps = DetectWalSegmentGaps(walFiles);

        // ── (d) pg_stat_archiver query ────────────────────────────────────────
        await QueryPgStatArchiverAsync(opts, ct);

        // ── (e) WAL segment integrity validation (edge case 1) ────────────────
        int corruptSegments = await ValidateRecentSegmentsAsync(opts, walFiles.Take(3).ToList(), ct);

        // ── Build and store status snapshot ──────────────────────────────────
        bool isHealthy = minutesSinceLast <= opts.MaxWalArchivalDelayMinutes
                         && detectedGaps == 0
                         && corruptSegments == 0;

        _latestStatus = new WalArchivalStatus
        {
            IsHealthy               = isHealthy,
            LastArchivedSegment     = latestFile?.Name,
            LastArchivedAtUtc       = lastArchivedAt,
            MinutesSinceLastArchive = minutesSinceLast,
            TotalArchivedSegments   = walFiles.Count,
            DetectedGaps            = detectedGaps,
            CorruptSegments         = corruptSegments,
            ArchiveDirectorySizeBytes = totalBytes,
            CapturedAtUtc           = DateTime.UtcNow,
        };

        // ── Retention cleanup (once per day) ──────────────────────────────────
        if (DateTime.UtcNow.Date > _lastRetentionCleanupDate)
        {
            await RunRetentionCleanupAsync(opts, ct);
            _lastRetentionCleanupDate = DateTime.UtcNow.Date;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Directory scan
    // ─────────────────────────────────────────────────────────────────────────

    private static (List<FileInfo> Files, long TotalBytes) ScanArchiveDirectory(string directory)
    {
        var files = new DirectoryInfo(directory)
            .EnumerateFiles()
            .ToList();

        long totalBytes = files.Sum(f => f.Length);
        return (files, totalBytes);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Gap detection
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses WAL segment filenames (<c>TTTTTTTTLLLLLLLLSSSSSSSS</c>), groups by timeline,
    /// and detects missing segments within each timeline's sequence.
    /// Returns the total count of detected gaps.
    /// </summary>
    private int DetectWalSegmentGaps(IEnumerable<FileInfo> walFiles)
    {
        // Group by (timeline, log-file number); segments within a log file are numbered sequentially.
        var byTimeline = walFiles
            .Select(f => WalSegmentRegex.Match(f.Name))
            .Where(m => m.Success)
            .Select(m => new
            {
                Timeline = Convert.ToUInt32(m.Groups["timeline"].Value, 16),
                Log      = Convert.ToUInt32(m.Groups["log"].Value,      16),
                Seg      = Convert.ToUInt32(m.Groups["seg"].Value,      16),
                FileName = m.Value,
            })
            .GroupBy(s => s.Timeline)
            .ToList();

        int totalGaps = 0;

        foreach (var timeline in byTimeline)
        {
            // Sort by (log, seg) and scan for gaps.
            var sorted = timeline
                .OrderBy(s => s.Log)
                .ThenBy(s => s.Seg)
                .ToList();

            for (int i = 1; i < sorted.Count; i++)
            {
                var prev = sorted[i - 1];
                var curr = sorted[i];

                // Segments are contiguous when they share the same log file and
                // seg numbers differ by exactly 1, or when log file increments by 1
                // and the new seg is 0.
                bool contiguous =
                    (prev.Log == curr.Log && curr.Seg == prev.Seg + 1) ||
                    (curr.Log == prev.Log + 1 && curr.Seg == 0);

                if (!contiguous)
                {
                    totalGaps++;
                    _logger.LogWarning(
                        "WAL_SEGMENT_GAP: Missing segments between {Start} and {End}, " +
                        "Timeline={Timeline}. PITR may not cover this period.",
                        prev.FileName, curr.FileName,
                        timeline.Key.ToString("X8"));
                }
            }
        }

        return totalGaps;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // pg_stat_archiver query
    // ─────────────────────────────────────────────────────────────────────────

    private async Task QueryPgStatArchiverAsync(WalArchivalOptions opts, CancellationToken ct)
    {
        var backupOpts = _backupOptions.CurrentValue;

        const string sql =
            "SELECT last_archived_wal, last_archived_time, " +
            "       failed_count, last_failed_wal, last_failed_time " +
            "FROM pg_stat_archiver;";

        var psi = new ProcessStartInfo
        {
            FileName               = opts.PsqlPath,
            Arguments              = $"--host={backupOpts.DbHost} " +
                                     $"--port={backupOpts.DbPort} " +
                                     $"--username={backupOpts.DbUsername} " +
                                     $"--dbname={backupOpts.DatabaseName} " +
                                     "--tuples-only --no-align " +
                                     $"--command=\"{sql}\"",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        psi.Environment["PGPASSWORD"] = backupOpts.DbPassword;

        try
        {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start psql for pg_stat_archiver.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                _logger.LogWarning(
                    "WalArchivalMonitoringService: pg_stat_archiver query failed (exit {Code}). " +
                    "Stderr={Stderr}",
                    process.ExitCode, stderr.Trim());
                return;
            }

            // Parse "failed_count" from psql output (column index 2).
            var line = stdout.Trim();
            if (!string.IsNullOrEmpty(line))
            {
                var parts = line.Split('|');
                if (parts.Length >= 5 &&
                    long.TryParse(parts[2].Trim(), out var failedCount) &&
                    failedCount > 0)
                {
                    _logger.LogError(
                        "WAL_ARCHIVE_FAILURES: FailedCount={FailedCount}, " +
                        "LastFailedWal={LastFailedWal}, LastFailedTime={LastFailedTime}",
                        failedCount,
                        parts[3].Trim(),
                        parts[4].Trim());
                }
                else
                {
                    _logger.LogDebug(
                        "WAL_STAT_ARCHIVER: LastArchivedWal={LastWal}, LastArchivedTime={LastTime}",
                        parts.Length > 0 ? parts[0].Trim() : "n/a",
                        parts.Length > 1 ? parts[1].Trim() : "n/a");
                }
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "WalArchivalMonitoringService: exception querying pg_stat_archiver.");
        }
        finally
        {
            if (psi.Environment.ContainsKey("PGPASSWORD"))
                psi.Environment["PGPASSWORD"] = string.Empty;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // WAL integrity validation (edge case 1)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs <c>pg_waldump --quiet</c> on the provided segments to verify they are parseable.
    /// Returns the count of corrupt segments found.
    /// </summary>
    private async Task<int> ValidateRecentSegmentsAsync(
        WalArchivalOptions  opts,
        List<FileInfo>      segments,
        CancellationToken   ct)
    {
        int corruptCount = 0;

        foreach (var segment in segments)
        {
            if (ct.IsCancellationRequested) break;

            var psi = new ProcessStartInfo
            {
                FileName               = opts.PgWaldumpPath,
                Arguments              = $"\"{segment.FullPath()}\" --quiet",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };

            try
            {
                using var process = Process.Start(psi);
                if (process is null)
                {
                    _logger.LogWarning(
                        "WalArchivalMonitoringService: failed to start pg_waldump for {File}.",
                        segment.Name);
                    continue;
                }

                var stderrTask = process.StandardError.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);
                var stderr = await stderrTask;

                if (process.ExitCode != 0)
                {
                    corruptCount++;
                    _logger.LogError(
                        "WAL_SEGMENT_CORRUPT: File={FileName}. " +
                        "PITR will fall back to most recent complete backup. " +
                        "Error={Error}",
                        segment.Name,
                        stderr.Trim());
                }
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex,
                    "WalArchivalMonitoringService: pg_waldump check failed for {File}.",
                    segment.Name);
            }
        }

        return corruptCount;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Retention cleanup
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Deletes WAL segments older than <see cref="WalArchivalOptions.WalRetentionDays"/>.
    /// Runs once per calendar day (guarded by <see cref="_lastRetentionCleanupDate"/>).
    /// </summary>
    private Task RunRetentionCleanupAsync(WalArchivalOptions opts, CancellationToken ct)
    {
        if (!Directory.Exists(opts.WalArchiveDirectory))
            return Task.CompletedTask;

        var cutoff       = DateTime.UtcNow.AddDays(-opts.WalRetentionDays);
        int deletedCount = 0;
        long freedBytes  = 0;
        DateTime? oldestRetained = null;

        try
        {
            var expiredFiles = new DirectoryInfo(opts.WalArchiveDirectory)
                .EnumerateFiles()
                .Where(f => WalSegmentRegex.IsMatch(f.Name) && f.LastWriteTimeUtc < cutoff)
                .OrderBy(f => f.LastWriteTimeUtc)
                .ToList();

            foreach (var file in expiredFiles)
            {
                if (ct.IsCancellationRequested) break;

                try
                {
                    long size = file.Length;
                    file.Delete();
                    deletedCount++;
                    freedBytes += size;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "WalArchivalMonitoringService: failed to delete expired WAL segment {File}.",
                        file.Name);
                }
            }

            // Find oldest retained segment for the summary log.
            oldestRetained = new DirectoryInfo(opts.WalArchiveDirectory)
                .EnumerateFiles()
                .Where(f => WalSegmentRegex.IsMatch(f.Name))
                .OrderBy(f => f.LastWriteTimeUtc)
                .FirstOrDefault()
                ?.LastWriteTimeUtc;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogError(ex,
                "WalArchivalMonitoringService: exception during WAL retention cleanup.");
            return Task.CompletedTask;
        }

        _logger.LogInformation(
            "WAL_RETENTION_CLEANUP: Deleted={Count}, FreedBytes={Bytes}, " +
            "OldestRetained={OldestDate}",
            deletedCount,
            freedBytes,
            oldestRetained?.ToString("yyyy-MM-dd") ?? "none");

        return Task.CompletedTask;
    }
}

/// <summary>
/// Extension to <see cref="FileInfo"/> that returns the <c>FullName</c> —
/// defined here to keep <c>segment.FullPath()</c> readable in the service.
/// </summary>
file static class FileInfoExtensions
{
    internal static string FullPath(this FileInfo fi) => fi.FullName;
}
