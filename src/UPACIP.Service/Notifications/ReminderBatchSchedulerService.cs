using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Production implementation of <see cref="IReminderBatchSchedulerService"/>.
///
/// Runs either the 24-hour or 2-hour reminder batch for the UTC appointment window
/// supplied in <see cref="ReminderBatchExecutionContext"/>:
///
/// <list type="number">
///   <item>Load the Redis checkpoint for this batch type + window date so an
///     interrupted run resumes from the last successful appointment (EC-1).</item>
///   <item>Query all eligible appointments in the window, ordered deterministically
///     by (AppointmentTime, Id).  Cancelled rows and appointments that already have a
///     successful <c>NotificationLog</c> entry for this notification type are excluded
///     at the database level.</item>
///   <item>Skip appointments that were processed in a previous partial run
///     (cursor-based resume).</item>
///   <item>Dispatch each appointment via <see cref="IReminderNotificationService"/>
///     and persist a checkpoint after each successful dispatch (EC-1).</item>
///   <item>Log structured metrics (processed, skipped, failed, duration) at INFO level
///     so operations can verify the AC-4 10-minute budget.</item>
/// </list>
///
/// <para><b>Time-budget guard (AC-4):</b> If the batch exceeds
/// <see cref="MaxBatchDuration"/> a warning is emitted and the run is marked
/// incomplete; the checkpoint allows the next invocation to resume.</para>
///
/// <para><b>Never throws.</b>  All outcomes are encoded in the returned
/// <see cref="ReminderBatchResult"/>.</para>
/// </summary>
public sealed class ReminderBatchSchedulerService : IReminderBatchSchedulerService
{
    /// <summary>
    /// Hard wall-clock budget per batch run aligned to the AC-4 10-minute SLA.
    /// A 30-second margin is reserved for metric logging and checkpoint cleanup.
    /// </summary>
    private static readonly TimeSpan MaxBatchDuration = TimeSpan.FromMinutes(9.5);

    private readonly ApplicationDbContext          _db;
    private readonly IReminderNotificationService  _reminderService;
    private readonly IDistributedCache             _cache;
    private readonly ILogger<ReminderBatchSchedulerService> _logger;

    public ReminderBatchSchedulerService(
        ApplicationDbContext                         db,
        IReminderNotificationService                 reminderService,
        IDistributedCache                            cache,
        ILogger<ReminderBatchSchedulerService>       logger)
    {
        _db              = db;
        _reminderService = reminderService;
        _cache           = cache;
        _logger          = logger;
    }

    /// <inheritdoc/>
    public async Task<ReminderBatchResult> RunBatchAsync(
        ReminderBatchExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        int processed = 0;
        int skipped   = 0;
        int failed    = 0;
        Guid? finalCheckpoint = null;
        bool completedFull    = false;

        try
        {
            _logger.LogInformation(
                "[REMINDER-BATCH-START] BatchType={BatchType} RunId={RunId} " +
                "Window=[{Start:O}, {End:O}) TZ={TimeZone}",
                context.BatchType, context.RunId,
                context.WindowStartUtc, context.WindowEndUtc, context.ClinicTimeZoneId);

            // ── 1. Resolve notification type ──────────────────────────────────
            var notificationType = context.BatchType == ReminderBatchType.TwentyFourHour
                ? NotificationType.Reminder24h
                : NotificationType.Reminder2h;

            // ── 2. Load checkpoint for this batch type + window date (EC-1) ──
            var checkpointKey  = BuildCheckpointKey(context.BatchType, context.WindowStartUtc);
            var checkpointCursor = await LoadCheckpointAsync(checkpointKey, cancellationToken);

            if (checkpointCursor.HasValue)
            {
                _logger.LogInformation(
                    "[REMINDER-BATCH-RESUME] Resuming after checkpoint {CheckpointId} " +
                    "BatchType={BatchType} RunId={RunId}",
                    checkpointCursor.Value, context.BatchType, context.RunId);
            }

            // ── 3. Query eligible appointments ────────────────────────────────
            // Excludes: cancelled appointments, appointments already successfully reminded
            // for this notification type.  Ordered deterministically for stable cursor resume.
            var rows = await _db.Appointments
                .AsNoTracking()
                .Where(a =>
                    a.AppointmentTime >= context.WindowStartUtc &&
                    a.AppointmentTime < context.WindowEndUtc &&
                    a.Status != AppointmentStatus.Cancelled &&
                    !_db.NotificationLogs.Any(n =>
                        n.AppointmentId == a.Id &&
                        n.NotificationType == notificationType &&
                        n.Status == NotificationStatus.Sent))
                .OrderBy(a => a.AppointmentTime)
                .ThenBy(a => a.Id)
                .Select(a => new AppointmentReminderRow(
                    a.Id,
                    a.PatientId,
                    a.Patient == null ? string.Empty : a.Patient.Email,
                    a.Patient == null ? string.Empty : a.Patient.PhoneNumber,
                    a.Patient == null ? "Patient" : a.Patient.FullName,
                    a.AppointmentTime,
                    a.ProviderName,
                    a.AppointmentType,
                    a.BookingReference))
                .ToListAsync(cancellationToken);

            _logger.LogInformation(
                "[REMINDER-BATCH-QUERIED] BatchType={BatchType} RunId={RunId} " +
                "Eligible={Count}",
                context.BatchType, context.RunId, rows.Count);

            // ── 4. Apply checkpoint cursor ────────────────────────────────────
            // Find where in the ordered list the last checkpoint falls and skip
            // everything up to and including it (those were already processed).
            var startIndex = 0;
            if (checkpointCursor.HasValue)
            {
                var idx = rows.FindIndex(r => r.AppointmentId == checkpointCursor.Value);
                startIndex = idx >= 0 ? idx + 1 : 0;
                skipped   += startIndex;  // count resumed-over records as skipped
            }

            // ── 5. Process eligible appointments ──────────────────────────────
            for (int i = startIndex; i < rows.Count; i++)
            {
                // AC-4 time-budget guard: abort before exceeding the 10-minute SLA
                if (stopwatch.Elapsed >= MaxBatchDuration)
                {
                    _logger.LogWarning(
                        "[REMINDER-BATCH-TIMEOUT] Batch exceeded {MaxMinutes} minutes. " +
                        "BatchType={BatchType} RunId={RunId} " +
                        "Processed={Processed} Remaining={Remaining}",
                        MaxBatchDuration.TotalMinutes, context.BatchType, context.RunId,
                        processed, rows.Count - i);
                    break;
                }

                // Honour host shutdown signal
                if (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(
                        "[REMINDER-BATCH-CANCELLED] Cancellation requested mid-batch. " +
                        "BatchType={BatchType} RunId={RunId} " +
                        "Processed={Processed} Remaining={Remaining}",
                        context.BatchType, context.RunId,
                        processed, rows.Count - i);
                    break;
                }

                var row = rows[i];
                var dispatchResult = await DispatchReminderAsync(row, context, cancellationToken);

                if (dispatchResult.CountsAsProcessed)
                {
                    processed++;
                    finalCheckpoint = row.AppointmentId;

                    // Persist checkpoint so a crash here lets the next run resume (EC-1)
                    await SaveCheckpointAsync(checkpointKey, row.AppointmentId, cancellationToken);
                }
                else
                {
                    failed++;
                    _logger.LogWarning(
                        "[REMINDER-BATCH-DISPATCH-FAIL] Appointment {AppointmentId} failed dispatch. " +
                        "Reason: {Reason} BatchType={BatchType} RunId={RunId}",
                        row.AppointmentId, dispatchResult.FailureReason,
                        context.BatchType, context.RunId);
                }
            }

            completedFull = (startIndex + processed + failed) >= rows.Count &&
                            !cancellationToken.IsCancellationRequested &&
                            stopwatch.Elapsed < MaxBatchDuration;

            // Clear checkpoint on successful full completion so the next day's run starts fresh
            if (completedFull)
                await _cache.RemoveAsync(checkpointKey, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[REMINDER-BATCH-ERROR] Unexpected error in batch. " +
                "BatchType={BatchType} RunId={RunId}",
                context.BatchType, context.RunId);
        }
        finally
        {
            stopwatch.Stop();

            _logger.LogInformation(
                "[REMINDER-BATCH-COMPLETE] BatchType={BatchType} RunId={RunId} " +
                "Processed={Processed} Skipped={Skipped} Failed={Failed} " +
                "Duration={DurationMs}ms Completed={Completed}",
                context.BatchType, context.RunId,
                processed, skipped, failed,
                (long)stopwatch.Elapsed.TotalMilliseconds,
                completedFull);
        }

        return new ReminderBatchResult(
            BatchType:         context.BatchType,
            ProcessedCount:    processed,
            SkippedCount:      skipped,
            FailedCount:       failed,
            FinalCheckpoint:   finalCheckpoint,
            Duration:          stopwatch.Elapsed,
            CompletedFullBatch: completedFull);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls <see cref="IReminderNotificationService.SendReminderAsync"/> and handles any
    /// unexpected exception by returning a failed result so the batch loop continues.
    /// </summary>
    private async Task<ReminderNotificationResult> DispatchReminderAsync(
        AppointmentReminderRow       row,
        ReminderBatchExecutionContext context,
        CancellationToken            ct)
    {
        var request = new ReminderNotificationRequest(
            AppointmentId:      row.AppointmentId,
            PatientId:          row.PatientId,
            PatientEmail:       row.PatientEmail,
            PatientPhoneNumber: row.PatientPhoneNumber,
            PatientFullName:    row.PatientFullName,
            AppointmentTimeUtc: row.AppointmentTimeUtc,
            ProviderName:       row.ProviderName,
            AppointmentType:    row.AppointmentType,
            BookingReference:   row.BookingReference,
            BatchType:          context.BatchType,
            CorrelationId:      context.RunId.ToString("N"));

        try
        {
            return await _reminderService.SendReminderAsync(request, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "ReminderNotificationService threw unexpectedly for appointment {AppointmentId}. " +
                "BatchType={BatchType} RunId={RunId}",
                row.AppointmentId, context.BatchType, context.RunId);

            return new ReminderNotificationResult(
                EmailSent: false, SmsSent: false, SmsSkippedOptOut: false,
                FailureReason: $"Unexpected dispatch exception: {ex.GetType().Name}");
        }
    }

    /// <summary>
    /// Builds a Redis checkpoint key scoped to a batch type and the UTC window date.
    /// Using the window date (not the run ID) ensures a restarted worker finds the
    /// same checkpoint for the same calendar-day batch run (EC-1).
    /// </summary>
    private static string BuildCheckpointKey(ReminderBatchType batchType, DateTime windowStartUtc)
        => $"reminder-ckpt:{(int)batchType}:{windowStartUtc:yyyyMMdd}";

    private async Task<Guid?> LoadCheckpointAsync(string key, CancellationToken ct)
    {
        try
        {
            var bytes = await _cache.GetAsync(key, ct);
            if (bytes is null || bytes.Length == 0)
                return null;

            var raw = Encoding.UTF8.GetString(bytes);
            return Guid.TryParse(raw, out var id) ? id : null;
        }
        catch (Exception ex)
        {
            // Non-fatal: treat as no checkpoint and restart from beginning.
            _logger.LogWarning(ex, "Could not load reminder batch checkpoint for key {Key}.", key);
            return null;
        }
    }

    private async Task SaveCheckpointAsync(string key, Guid appointmentId, CancellationToken ct)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(appointmentId.ToString());
            await _cache.SetAsync(key, bytes,
                new DistributedCacheEntryOptions
                {
                    // Keep checkpoint for 2 days to survive overnight restarts
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(2),
                },
                ct);
        }
        catch (Exception ex)
        {
            // Non-fatal: checkpoint loss means a restart re-processes already-sent
            // appointments, which the NotificationLog deduplication will skip anyway.
            _logger.LogWarning(ex,
                "Could not save reminder batch checkpoint {AppointmentId} for key {Key}.",
                appointmentId, key);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private projection type (query output only; not exposed outside this class)
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record AppointmentReminderRow(
        Guid     AppointmentId,
        Guid     PatientId,
        string   PatientEmail,
        string   PatientPhoneNumber,
        string   PatientFullName,
        DateTime AppointmentTimeUtc,
        string?  ProviderName,
        string?  AppointmentType,
        string?  BookingReference);
}
