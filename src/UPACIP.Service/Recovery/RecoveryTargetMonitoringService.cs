using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.Service.Backup;
using UPACIP.Service.Recovery.Models;

namespace UPACIP.Service.Recovery;

/// <summary>
/// BackgroundService that continuously monitors the system's RPO/RTO compliance
/// (US_095, AC-3, NFR-024, NFR-025).
///
/// <para>
/// On each check cycle (every <see cref="RecoveryTargetOptions.MonitoringCheckIntervalMinutes"/>):
/// <list type="number">
///   <item>RPO check — queries <c>BackupLogs</c> and the WAL archival service for the most
///        recent data protection timestamps; emits <c>RPO_VIOLATION</c> Critical log when the
///        gap exceeds the configured target (NFR-024, DR-027).</item>
///   <item>RTO readiness check — validates that backup, WAL, Active runbook, and quarterly
///        test preconditions are all in place; emits <c>RTO_READINESS_GAP</c> Warning when
///        any condition is not met (NFR-025).</item>
///   <item>Status snapshot — replaces the in-memory <see cref="LatestStatus"/> reference
///        atomically so the admin API always returns the most recent assessment.</item>
/// </list>
/// </para>
///
/// <para>
/// Registered as a Singleton HostedService + concrete-type Singleton so the admin
/// <c>RecoveryController</c> can inject it directly to read <see cref="LatestStatus"/>
/// without a database round-trip (same pattern used by <c>WalArchivalMonitoringService</c>).
/// </para>
/// </summary>
public sealed class RecoveryTargetMonitoringService : BackgroundService
{
    private readonly IServiceScopeFactory           _scopeFactory;
    private readonly WalArchivalMonitoringService   _walMonitor;
    private readonly IOptionsMonitor<RecoveryTargetOptions> _optionsMonitor;
    private readonly ILogger<RecoveryTargetMonitoringService> _logger;

    // Volatile reference so the API thread always reads the latest value without locking.
    private volatile RecoveryTargetStatus? _latestStatus;

    /// <summary>
    /// The most recent RPO/RTO compliance snapshot. Null until the first check cycle completes.
    /// </summary>
    public RecoveryTargetStatus? LatestStatus => _latestStatus;

    public RecoveryTargetMonitoringService(
        IServiceScopeFactory                          scopeFactory,
        WalArchivalMonitoringService                  walMonitor,
        IOptionsMonitor<RecoveryTargetOptions>        optionsMonitor,
        ILogger<RecoveryTargetMonitoringService>      logger)
    {
        _scopeFactory    = scopeFactory;
        _walMonitor      = walMonitor;
        _optionsMonitor  = optionsMonitor;
        _logger          = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "RECOVERY_MONITOR_STARTED: RPO target={RpoMin}min, RTO target={RtoMin}min, " +
            "CheckInterval={Interval}min.",
            _optionsMonitor.CurrentValue.RpoMinutes,
            _optionsMonitor.CurrentValue.RtoMinutes,
            _optionsMonitor.CurrentValue.MonitoringCheckIntervalMinutes);

        // Run immediately on startup, then on interval.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCheckCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "RECOVERY_MONITOR_ERROR: Unhandled exception during check cycle.");
            }

            var opts = _optionsMonitor.CurrentValue;
            await Task.Delay(TimeSpan.FromMinutes(opts.MonitoringCheckIntervalMinutes), stoppingToken);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────

    private async Task RunCheckCycleAsync(CancellationToken ct)
    {
        var opts     = _optionsMonitor.CurrentValue;
        var warnings = new List<string>();
        var now      = DateTime.UtcNow;

        // ── (a) RPO check (NFR-024) ───────────────────────────────────────────
        DateTime lastBackupUtc;
        DateTime lastWalArchiveUtc;

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var mostRecentBackup = await db.BackupLogs
                .Where(b => b.Status == "Completed")
                .OrderByDescending(b => b.CreatedAtUtc)
                .Select(b => (DateTime?)b.CreatedAtUtc)
                .FirstOrDefaultAsync(ct);

            lastBackupUtc = mostRecentBackup ?? DateTime.MinValue;
        }

        var walStatus     = _walMonitor.GetArchivalStatus();
        lastWalArchiveUtc = walStatus.LastArchivedAtUtc ?? DateTime.MinValue;

        // Current RPO = time since the most recent data protection event.
        // WAL archival is more granular than backup, so we use it as primary signal.
        var mostRecentProtection = lastWalArchiveUtc > lastBackupUtc
            ? lastWalArchiveUtc
            : lastBackupUtc;

        var currentRpo   = mostRecentProtection == DateTime.MinValue
            ? TimeSpan.MaxValue
            : now - mostRecentProtection;

        var targetRpo    = TimeSpan.FromMinutes(opts.RpoMinutes);
        var rpoCompliant = currentRpo <= targetRpo;

        if (!rpoCompliant)
        {
            var msg = $"RPO_VIOLATION: CurrentRPO={currentRpo.TotalMinutes:F1}min exceeds target {opts.RpoMinutes}min. " +
                      $"LastWAL={lastWalArchiveUtc:O}, LastBackup={lastBackupUtc:O}";
            _logger.LogCritical(msg);
            warnings.Add(msg);
        }
        else
        {
            _logger.LogDebug(
                "RPO_CHECK_PASSED: CurrentRPO={Minutes:F1}min (target {Target}min).",
                currentRpo.TotalMinutes, opts.RpoMinutes);
        }

        // ── (b) RTO readiness check (NFR-025) ─────────────────────────────────
        var missingComponents = new List<string>();

        // 1. Backup must be recent (within BackupFrequencyHours).
        if (lastBackupUtc == DateTime.MinValue ||
            now - lastBackupUtc > TimeSpan.FromHours(opts.BackupFrequencyHours))
        {
            missingComponents.Add($"Backup older than {opts.BackupFrequencyHours}h");
            warnings.Add($"RTO_READINESS: No backup within last {opts.BackupFrequencyHours} hours.");
        }

        // 2. WAL archives must be current (within WalArchiveIntervalMinutes).
        if (lastWalArchiveUtc == DateTime.MinValue ||
            now - lastWalArchiveUtc > TimeSpan.FromMinutes(opts.WalArchiveIntervalMinutes))
        {
            missingComponents.Add($"WAL archive gap > {opts.WalArchiveIntervalMinutes}min");
            warnings.Add($"RTO_READINESS: WAL archive is stale (last archived {lastWalArchiveUtc:O}).");
        }

        // 3. At least one Active disaster recovery runbook must exist.
        bool hasActiveRunbook;
        // 4. Quarterly test must not be overdue.
        string quarterlyStatus;
        DateTime? lastTestUtc;
        DateTime nextDeadlineUtc;

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            hasActiveRunbook = await db.DisasterRecoveryRunbooks
                .AnyAsync(r => r.Status == "Active", ct);

            var currentQuarter = GetCurrentQuarter(now);
            lastTestUtc = await db.RecoveryTestRecords
                .Where(t => t.Quarter == currentQuarter && t.Passed)
                .OrderByDescending(t => t.ExecutedAtUtc)
                .Select(t => (DateTime?)t.ExecutedAtUtc)
                .FirstOrDefaultAsync(ct);

            nextDeadlineUtc = GetQuarterEndUtc(now);
            quarterlyStatus = DetermineQuarterlyStatus(lastTestUtc, nextDeadlineUtc, opts, now);
        }

        if (!hasActiveRunbook)
        {
            missingComponents.Add("No Active disaster recovery runbook");
            warnings.Add("RTO_READINESS: No Active disaster recovery runbook found.");
        }

        if (quarterlyStatus == "Overdue")
        {
            missingComponents.Add("Quarterly recovery test overdue");
            warnings.Add($"RTO_READINESS: Quarterly recovery test overdue (deadline {nextDeadlineUtc:O}).");
        }

        var rtoCompliant = missingComponents.Count == 0;
        if (!rtoCompliant)
        {
            _logger.LogWarning(
                "RTO_READINESS_GAP: MissingComponents={Components}",
                string.Join("; ", missingComponents));
        }

        // ── (c) Persist status snapshot ───────────────────────────────────────
        _latestStatus = new RecoveryTargetStatus
        {
            RpoCompliant               = rpoCompliant,
            CurrentRpo                 = currentRpo == TimeSpan.MaxValue ? TimeSpan.MaxValue : currentRpo,
            TargetRpo                  = targetRpo,
            RtoCompliant               = rtoCompliant,
            TargetRto                  = TimeSpan.FromMinutes(opts.RtoMinutes),
            LastBackupUtc              = lastBackupUtc == DateTime.MinValue ? DateTime.UnixEpoch : lastBackupUtc,
            LastWalArchiveUtc          = lastWalArchiveUtc == DateTime.MinValue ? DateTime.UnixEpoch : lastWalArchiveUtc,
            LastRecoveryTestUtc        = lastTestUtc,
            NextRecoveryTestDeadlineUtc = nextDeadlineUtc,
            QuarterlyTestStatus        = quarterlyStatus,
            Warnings                   = warnings.AsReadOnly(),
            CheckedAtUtc               = now,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    internal static string GetCurrentQuarter(DateTime utc)
    {
        var q = (utc.Month - 1) / 3 + 1;
        return $"{utc.Year}-Q{q}";
    }

    internal static DateTime GetQuarterEndUtc(DateTime utc)
    {
        var q    = (utc.Month - 1) / 3 + 1;
        var endMonth = q * 3;
        var lastDay  = DateTime.DaysInMonth(utc.Year, endMonth);
        return new DateTime(utc.Year, endMonth, lastDay, 23, 59, 59, DateTimeKind.Utc);
    }

    internal static string DetermineQuarterlyStatus(
        DateTime?                   lastTestUtc,
        DateTime                    deadlineUtc,
        RecoveryTargetOptions       opts,
        DateTime                    now)
    {
        if (lastTestUtc.HasValue) return "Current";
        if (now > deadlineUtc)   return "Overdue";
        return (deadlineUtc - now).TotalDays <= opts.QuarterlyTestAlertDaysBefore
            ? "DueSoon"
            : "Current";
    }
}
