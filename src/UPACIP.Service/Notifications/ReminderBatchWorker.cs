using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UPACIP.Service.Appointments;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Timer-based background worker that drives the 24-hour and 2-hour reminder batches
/// on a schedule aligned to the clinic's local timezone (US_035 AC-1, AC-2, EC-2).
///
/// <para><b>24-hour batch (AC-1):</b> Runs once per clinic-local calendar day at
/// <see cref="TwentyFourHourRunTimeLocalHour"/>:00.  The query window covers the full
/// next clinic-local calendar day (midnight-to-midnight) converted to UTC, so all
/// appointments scheduled "tomorrow" receive an email + SMS reminder.</para>
///
/// <para><b>2-hour batch (AC-2):</b> Runs every
/// <see cref="TwoHourCadenceMinutes"/> minutes.  The query window is
/// [now+1h30m, now+2h30m] UTC, giving a ±30-minute tolerance around the 2-hour
/// mark so no appointment is missed between cadence ticks.</para>
///
/// <para><b>Scheduling alignment (EC-2):</b> Both run-time decisions use the clinic's
/// IANA timezone from <see cref="ClinicSettings.TimeZoneId"/> so "tomorrow" and
/// "today" are always interpreted in clinic-local time, not UTC.</para>
///
/// <para><b>Batch execution:</b> A fresh DI scope is created per batch run so
/// scoped services (ApplicationDbContext, IReminderBatchSchedulerService) are isolated
/// and disposed correctly after each run — matching the WaitlistOfferProcessor pattern
/// already established in the codebase.</para>
/// </summary>
public sealed class ReminderBatchWorker : BackgroundService
{
    // ── Schedule constants ────────────────────────────────────────────────────

    /// <summary>
    /// Clinic-local hour at which the 24-hour reminder batch runs each day.
    /// Default 8 (08:00) gives patients roughly 16–40 hours notice before their
    /// appointment, covering same-day-next-day scenarios (AC-1).
    /// </summary>
    private const int TwentyFourHourRunTimeLocalHour = 8;

    /// <summary>
    /// How often (minutes) the 2-hour reminder batch runs.
    /// Default 30 ensures appointments within a ±30-minute window of the 2-hour mark
    /// are never missed (AC-2).
    /// </summary>
    private const int TwoHourCadenceMinutes = 30;

    /// <summary>
    /// Half-width (minutes) of the 2-hour query window.
    /// The query covers [now+2h-HalfWidth, now+2h+HalfWidth), preventing gaps
    /// between cadence ticks.
    /// </summary>
    private const int TwoHourWindowHalfWidthMinutes = 30;

    /// <summary>How frequently the worker wakes up to evaluate whether a batch is due.</summary>
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    // ── State tracking ────────────────────────────────────────────────────────

    /// <summary>
    /// Clinic-local calendar date for which the 24-hour batch was last run.
    /// Initialized to <see cref="DateTime.MinValue"/> so the batch always fires on the
    /// first eligible tick after startup.
    /// </summary>
    private DateTime _last24hRunLocalDate = DateTime.MinValue;

    /// <summary>
    /// UTC timestamp of the last 2-hour batch run.
    /// Initialized to <see cref="DateTime.MinValue"/> so the batch fires on the
    /// first eligible tick after startup.
    /// </summary>
    private DateTime _last2hRunUtc = DateTime.MinValue;

    private readonly IServiceScopeFactory     _scopeFactory;
    private readonly ClinicSettings           _clinicSettings;
    private readonly ILogger<ReminderBatchWorker> _logger;

    public ReminderBatchWorker(
        IServiceScopeFactory        scopeFactory,
        ClinicSettings              clinicSettings,
        ILogger<ReminderBatchWorker> logger)
    {
        _scopeFactory    = scopeFactory;
        _clinicSettings  = clinicSettings;
        _logger          = logger;
    }

    /// <summary>
    /// Main loop: wakes every minute, evaluates whether either batch type is due,
    /// and runs it in a fresh DI scope.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ReminderBatchWorker started. TZ={TimeZoneId} " +
            "24h-run-hour={Hour}:00 2h-cadence={Cadence}min",
            _clinicSettings.TimeZoneId,
            TwentyFourHourRunTimeLocalHour,
            TwoHourCadenceMinutes);

        using var timer = new PeriodicTimer(TickInterval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var nowUtc        = DateTime.UtcNow;
                var clinicTz      = ResolveTimeZone(_clinicSettings.TimeZoneId);
                var nowLocal      = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, clinicTz);

                await TryRun24hBatchAsync(nowLocal, nowUtc, clinicTz, stoppingToken);
                await TryRun2hBatchAsync(nowUtc, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on host shutdown — do not rethrow
        }

        _logger.LogInformation("ReminderBatchWorker stopped.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 24-hour batch scheduling
    // ─────────────────────────────────────────────────────────────────────────

    private async Task TryRun24hBatchAsync(
        DateTime          nowLocal,
        DateTime          nowUtc,
        TimeZoneInfo      clinicTz,
        CancellationToken ct)
    {
        // Run once per clinic-local calendar day at the configured hour (EC-2)
        var todayLocal = nowLocal.Date;

        if (_last24hRunLocalDate >= todayLocal)
            return;  // Already ran today

        if (nowLocal.Hour < TwentyFourHourRunTimeLocalHour)
            return;  // Not yet time today

        // Window = full next clinic-local calendar day converted to UTC
        var tomorrowLocalMidnight     = todayLocal.AddDays(1);
        var dayAfterTomorrowMidnight  = todayLocal.AddDays(2);

        var windowStartUtc = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(tomorrowLocalMidnight,    DateTimeKind.Unspecified), clinicTz);
        var windowEndUtc   = TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(dayAfterTomorrowMidnight, DateTimeKind.Unspecified), clinicTz);

        var context = new ReminderBatchExecutionContext(
            BatchType:        ReminderBatchType.TwentyFourHour,
            WindowStartUtc:   windowStartUtc,
            WindowEndUtc:     windowEndUtc,
            ClinicTimeZoneId: _clinicSettings.TimeZoneId,
            RunId:            Guid.NewGuid());

        await RunBatchInScopeAsync(context, ct);

        _last24hRunLocalDate = todayLocal;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2-hour batch scheduling
    // ─────────────────────────────────────────────────────────────────────────

    private async Task TryRun2hBatchAsync(DateTime nowUtc, CancellationToken ct)
    {
        var minutesSinceLast = (nowUtc - _last2hRunUtc).TotalMinutes;

        if (minutesSinceLast < TwoHourCadenceMinutes)
            return;

        // Window: [now+2h-halfWidth, now+2h+halfWidth)
        var midpoint       = nowUtc.AddHours(2);
        var halfWidth      = TimeSpan.FromMinutes(TwoHourWindowHalfWidthMinutes);
        var windowStartUtc = midpoint - halfWidth;
        var windowEndUtc   = midpoint + halfWidth;

        var context = new ReminderBatchExecutionContext(
            BatchType:        ReminderBatchType.TwoHour,
            WindowStartUtc:   windowStartUtc,
            WindowEndUtc:     windowEndUtc,
            ClinicTimeZoneId: _clinicSettings.TimeZoneId,
            RunId:            Guid.NewGuid());

        await RunBatchInScopeAsync(context, ct);

        _last2hRunUtc = nowUtc;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Scoped batch execution
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a fresh DI scope, resolves <see cref="IReminderBatchSchedulerService"/>,
    /// and executes the batch.  Any unhandled exception is caught here so the worker
    /// loop continues to the next tick.
    /// </summary>
    private async Task RunBatchInScopeAsync(
        ReminderBatchExecutionContext context,
        CancellationToken             ct)
    {
        try
        {
            using var scope     = _scopeFactory.CreateScope();
            var schedulerService = scope.ServiceProvider
                .GetRequiredService<IReminderBatchSchedulerService>();

            var result = await schedulerService.RunBatchAsync(context, ct);

            // AC-4: warn if batch exceeded the 10-minute SLA
            if (result.Duration > TimeSpan.FromMinutes(10))
            {
                _logger.LogWarning(
                    "[REMINDER-WORKER-OVERRUN] Batch exceeded 10-minute SLA. " +
                    "BatchType={BatchType} RunId={RunId} Duration={DurationMs}ms",
                    result.BatchType, context.RunId,
                    (long)result.Duration.TotalMilliseconds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[REMINDER-WORKER-ERROR] Unhandled error running batch. " +
                "BatchType={BatchType} RunId={RunId}",
                context.BatchType, context.RunId);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Timezone helper
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a <see cref="TimeZoneInfo"/> from an IANA ID, with a safe fallback
    /// to UTC when the ID is unrecognised (e.g. on a Windows host without TZ data).
    /// </summary>
    private TimeZoneInfo ResolveTimeZone(string ianaId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(ianaId);
        }
        catch (TimeZoneNotFoundException)
        {
            _logger.LogWarning(
                "Timezone '{TimeZoneId}' not found on this host. Falling back to UTC " +
                "for reminder scheduling. This may cause incorrect reminder windows (EC-2).",
                ianaId);
            return TimeZoneInfo.Utc;
        }
    }
}
