using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Retention.Models;

namespace UPACIP.Service.Retention;

/// <summary>
/// Nightly BackgroundService that purges <c>NotificationLog</c> entries older than the
/// configured retention threshold (US_086 AC-4, DR-019).
///
/// Scheduling:
/// <list type="bullet">
///   <item>Parses <see cref="RetentionPolicyOptions.NightlyJobScheduleLocal"/> (e.g. <c>"03:00"</c>).</item>
///   <item>Converts the next scheduled local time to UTC and waits via
///         <see cref="Task.Delay(TimeSpan, CancellationToken)"/>.</item>
///   <item>If the target time has already passed for today, schedules for tomorrow.</item>
/// </list>
///
/// Purge behaviour:
/// <list type="bullet">
///   <item>Queries candidates in batches of <see cref="RetentionPolicyOptions.PurgeBatchSize"/>
///         to avoid long-running transactions.</item>
///   <item>Skips any record protected by an audit log reference within the 7-year window
///         when <see cref="RetentionPolicyOptions.EnforceAuditLogReferenceProtection"/> is true
///         (edge case 2).</item>
///   <item>Uses EF Core 8 bulk-delete (<c>ExecuteDeleteAsync</c>) — no entity tracking overhead.</item>
///   <item>Emits a structured <c>RETENTION_PURGE_COMPLETE</c> log event with per-type counts (AC-4).</item>
///   <item>On failure: logs <c>RETENTION_PURGE_FAILED</c> and continues to the next cycle
///         without retry (avoids cascading failures during DB issues).</item>
/// </list>
///
/// Scoped services (<see cref="ApplicationDbContext"/>, <see cref="IRetentionPolicyGuard"/>)
/// are resolved per-cycle via <see cref="IServiceScopeFactory"/> following the .NET BackgroundService
/// pattern (see <c>UptimeMonitoringService</c>).
/// </summary>
public sealed class DataRetentionService : BackgroundService
{
    private readonly IServiceScopeFactory                    _scopeFactory;
    private readonly IOptionsMonitor<RetentionPolicyOptions> _options;
    private readonly ILogger<DataRetentionService>           _logger;

    public DataRetentionService(
        IServiceScopeFactory                    scopeFactory,
        IOptionsMonitor<RetentionPolicyOptions> options,
        ILogger<DataRetentionService>           logger)
    {
        _scopeFactory = scopeFactory;
        _options      = options;
        _logger       = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // BackgroundService loop
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "DataRetentionService started. NightlyJobScheduleLocal={Schedule}",
            _options.CurrentValue.NightlyJobScheduleLocal);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Re-read options every iteration so changes in appsettings.json take effect
            // on the next scheduled cycle (edge case 1).
            var nextRunUtc = ComputeNextRunUtc(_options.CurrentValue.NightlyJobScheduleLocal);
            var delay      = nextRunUtc - DateTime.UtcNow;

            if (delay > TimeSpan.Zero)
            {
                _logger.LogDebug(
                    "DataRetentionService: next purge cycle scheduled at {NextRunUtc} UTC " +
                    "(delay {DelayMinutes:F0} minutes).",
                    nextRunUtc, delay.TotalMinutes);

                try
                {
                    await Task.Delay(delay, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
            }

            // ── Execute purge cycle ─────────────────────────────────────────
            try
            {
                await RunPurgeCycleAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Log failure and continue to next cycle — do not crash the host.
                _logger.LogError(ex,
                    "RETENTION_PURGE_FAILED: Unhandled exception during purge cycle. " +
                    "Will retry at next scheduled time.");
            }

            // ── Execute archival cycle (after purge so notification logs are cleared first) ──
            try
            {
                await RunArchivalCycleAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex,
                    "RETENTION_ARCHIVAL_FAILED: Unhandled exception during archival cycle. " +
                    "Will retry at next scheduled time.");
            }

            // Small guard delay so a very fast clock-skew cannot spin tight if ComputeNextRunUtc
            // returns a time that is immediately in the past again.
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken)
                .ContinueWith(_ => { }, TaskContinuationOptions.OnlyOnCanceled);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Purge cycle
    // ─────────────────────────────────────────────────────────────────────────

    private async Task RunPurgeCycleAsync(CancellationToken ct)
    {
        var opts    = _options.CurrentValue;
        var cutoff  = DateTime.UtcNow.AddDays(-opts.NotificationLogRetentionDays);
        var sw      = Stopwatch.StartNew();

        _logger.LogInformation(
            "DataRetentionService: starting notification log purge. Cutoff={Cutoff:o} BatchSize={BatchSize}",
            cutoff, opts.PurgeBatchSize);

        int totalPurged = 0;
        DateTime? oldestPurged = null;
        DateTime? newestPurged = null;
        var typeBreakdown = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        bool hasMore = true;
        while (hasMore && !ct.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var db    = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var guard = scope.ServiceProvider.GetRequiredService<IRetentionPolicyGuard>();

            // ── Load next batch of candidate notification IDs ─────────────────
            // Load minimal projection to reduce memory footprint.
            var candidates = await db.NotificationLogs
                .AsNoTracking()
                .Where(n => n.CreatedAt < cutoff)
                .OrderBy(n => n.CreatedAt)        // oldest-first for meaningful date tracking
                .Select(n => new { n.NotificationId, n.CreatedAt, n.NotificationType })
                .Take(opts.PurgeBatchSize)
                .ToListAsync(ct);

            if (candidates.Count == 0)
            {
                hasMore = false;
                break;
            }

            // ── Filter out audit-log-protected records (edge case 2) ──────────
            var deletableIds    = new List<Guid>(candidates.Count);
            var resourceType    = "NotificationLog";

            foreach (var candidate in candidates)
            {
                bool canDelete = await guard.CanDeleteAsync(
                    RetentionCategory.Notifications, candidate.CreatedAt, ct);

                if (!canDelete) continue;

                if (opts.EnforceAuditLogReferenceProtection)
                {
                    bool auditProtected = await guard.IsProtectedByAuditLogAsync(
                        resourceType, candidate.NotificationId, ct);
                    if (auditProtected) continue;
                }

                deletableIds.Add(candidate.NotificationId);

                // Track date range of purged records.
                if (oldestPurged is null || candidate.CreatedAt < oldestPurged)
                    oldestPurged = candidate.CreatedAt;
                if (newestPurged is null || candidate.CreatedAt > newestPurged)
                    newestPurged = candidate.CreatedAt;

                // Accumulate per-type breakdown.
                var typeKey = candidate.NotificationType.ToString();
                typeBreakdown.TryGetValue(typeKey, out var existing);
                typeBreakdown[typeKey] = existing + 1;
            }

            if (deletableIds.Count > 0)
            {
                // EF Core 8 bulk-delete — no entity materialisation or change-tracking overhead.
                var deletedCount = await db.NotificationLogs
                    .Where(n => deletableIds.Contains(n.NotificationId))
                    .ExecuteDeleteAsync(ct);

                totalPurged += deletedCount;

                _logger.LogDebug(
                    "DataRetentionService: batch deleted {Count} notification log records.",
                    deletedCount);
            }

            // If the full batch was non-deletable (all audit-protected), stop iterating to
            // prevent an infinite loop over the same protected records.
            hasMore = candidates.Count == opts.PurgeBatchSize
                   && deletableIds.Count > 0;
        }

        sw.Stop();

        var result = new PurgeResult
        {
            Category          = RetentionCategory.Notifications,
            RecordsPurged     = totalPurged,
            OldestPurgedDate  = oldestPurged,
            NewestPurgedDate  = newestPurged,
            ExecutionDuration = sw.Elapsed,
            BreakdownByType   = typeBreakdown,
        };

        EmitPurgeSummary(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Archival cycle (US_086 task_002, AC-3, AC-5)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task RunArchivalCycleAsync(CancellationToken ct)
    {
        _logger.LogInformation("DataRetentionService: starting appointment archival cycle.");

        using var scope    = _scopeFactory.CreateScope();
        var archivalService = scope.ServiceProvider.GetRequiredService<IAppointmentArchivalService>();

        // Step 2: Archive completed appointments older than AppointmentRetentionYears (AC-3).
        var completedResult = await archivalService.ArchiveCompletedAppointmentsAsync(ct);
        EmitArchivalSummaryLog(completedResult);

        // Step 3: Archive cancelled appointments older than CancelledAppointmentRetentionYears (AC-5).
        var cancelledResult = await archivalService.ArchiveCancelledAppointmentsAsync(ct);
        EmitArchivalSummaryLog(cancelledResult);

        // Step 4: Archive soft-deleted patients whose DeletedAt exceeds SoftDeletedPatientArchivalDays (US_087 AC-4, DR-021).
        // Resolved in a separate scope because PatientArchivalService owns its own
        // long-running transaction and should not share the appointment scope's DbContext.
        using var patientScope    = _scopeFactory.CreateScope();
        var patientArchivalService = patientScope.ServiceProvider
            .GetRequiredService<IPatientArchivalService>();

        var patientResult = await patientArchivalService.ArchiveSoftDeletedPatientsAsync(ct);
        EmitArchivalSummaryLog(patientResult);
    }

    private void EmitArchivalSummaryLog(ArchivalResult result)
    {
        if (result.RecordsArchived == 0
            && result.RecordsSkippedAuditProtected == 0
            && result.RecordsSkippedActiveFk == 0)
        {
            // Already logged at Debug level inside AppointmentArchivalService; no duplicate needed.
            return;
        }

        _logger.LogInformation(
            "RETENTION_ARCHIVAL_COMPLETE: Category={Category}, Archived={Archived}, " +
            "Skipped(AuditProtected)={AuditSkipped}, Skipped(ActiveFK)={FkSkipped}, " +
            "Duration={DurationMs}ms",
            result.Category,
            result.RecordsArchived,
            result.RecordsSkippedAuditProtected,
            result.RecordsSkippedActiveFk,
            (int)result.ExecutionDuration.TotalMilliseconds);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void EmitPurgeSummary(PurgeResult result)
    {
        if (result.RecordsPurged == 0)
        {
            _logger.LogDebug(
                "RETENTION_PURGE_COMPLETE: Category={Category}, RecordsPurged=0, " +
                "Duration={DurationMs}ms. No records eligible for purge.",
                result.Category, (int)result.ExecutionDuration.TotalMilliseconds);
            return;
        }

        _logger.LogInformation(
            "RETENTION_PURGE_COMPLETE: Category={Category}, RecordsPurged={Count}, " +
            "OldestPurged={OldestDate:o}, NewestPurged={NewestDate:o}, " +
            "Duration={DurationMs}ms, Breakdown={@Breakdown}",
            result.Category,
            result.RecordsPurged,
            result.OldestPurgedDate,
            result.NewestPurgedDate,
            (int)result.ExecutionDuration.TotalMilliseconds,
            result.BreakdownByType);
    }

    /// <summary>
    /// Computes the next UTC execution time from the configured local-time string.
    /// If the target time has already passed today in local time, schedules for tomorrow.
    /// </summary>
    private static DateTime ComputeNextRunUtc(string scheduleLocal)
    {
        // Parse "HH:mm" format.
        if (!TimeSpan.TryParseExact(scheduleLocal, @"hh\:mm", null, out var timeOfDay))
        {
            // Fallback to 03:00 AM if the configured value is malformed.
            timeOfDay = new TimeSpan(3, 0, 0);
        }

        var localNow    = DateTime.Now;
        var localTarget = localNow.Date.Add(timeOfDay);

        // If already past today's target, schedule for tomorrow.
        if (localTarget <= localNow)
            localTarget = localTarget.AddDays(1);

        return localTarget.ToUniversalTime();
    }
}
