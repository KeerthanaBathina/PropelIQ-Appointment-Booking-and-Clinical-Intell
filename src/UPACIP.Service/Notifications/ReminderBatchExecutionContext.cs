namespace UPACIP.Service.Notifications;

/// <summary>
/// Identifies which reminder window a batch run covers (US_035 AC-1, AC-2).
/// </summary>
public enum ReminderBatchType
{
    /// <summary>
    /// Sends a personalized email and SMS for appointments scheduled during tomorrow's
    /// clinic-local calendar day (AC-1).
    /// </summary>
    TwentyFourHour,

    /// <summary>
    /// Sends an SMS-only reminder for appointments scheduled approximately 2 hours
    /// from the time the batch runs (AC-2).
    /// </summary>
    TwoHour,
}

/// <summary>
/// Input context supplied to <see cref="IReminderBatchSchedulerService.RunBatchAsync"/>
/// describing the batch type, UTC appointment window, and run identity.
///
/// <para><b>Window semantics:</b></para>
/// <list type="bullet">
///   <item>For <see cref="ReminderBatchType.TwentyFourHour"/> the window spans the full
///     next clinic-local calendar day, expressed in UTC by the caller.</item>
///   <item>For <see cref="ReminderBatchType.TwoHour"/> the window spans
///     [now+1h30m, now+2h30m] UTC so a 30-minute batch cadence never misses
///     the 2-hour mark for any appointment.</item>
/// </list>
///
/// <para><b>Timezone (EC-2):</b> The caller converts to UTC before populating this
/// record.  The scheduler operates entirely in UTC internally; <see cref="ClinicTimeZoneId"/>
/// is preserved for checkpoint key derivation and structured log output only.</para>
/// </summary>
public sealed record ReminderBatchExecutionContext(
    /// <summary>Whether this is a 24-hour or 2-hour reminder batch.</summary>
    ReminderBatchType BatchType,

    /// <summary>UTC start of the appointment window (inclusive).</summary>
    DateTime WindowStartUtc,

    /// <summary>UTC end of the appointment window (exclusive).</summary>
    DateTime WindowEndUtc,

    /// <summary>IANA timezone ID of the clinic (e.g. "America/New_York").</summary>
    string ClinicTimeZoneId,

    /// <summary>Unique identifier for this run used in structured log correlation.</summary>
    Guid RunId);

/// <summary>
/// Outcome returned by <see cref="IReminderBatchSchedulerService.RunBatchAsync"/>
/// after a complete or interrupted batch run.
/// </summary>
public sealed record ReminderBatchResult(
    /// <summary>Which batch type was processed.</summary>
    ReminderBatchType BatchType,

    /// <summary>Number of appointments for which dispatch was successfully attempted.</summary>
    int ProcessedCount,

    /// <summary>
    /// Number of appointments skipped because they were already reminded or ineligible.
    /// </summary>
    int SkippedCount,

    /// <summary>
    /// Number of appointments where the notification service returned a failure result.
    /// Email-without-SMS partial successes are counted as processed, not failed.
    /// </summary>
    int FailedCount,

    /// <summary>
    /// <c>Id</c> of the last appointment that was successfully processed.
    /// Stored in Redis so a restart can resume from the next record (EC-1).
    /// <c>null</c> when no appointments were processed.
    /// </summary>
    Guid? FinalCheckpoint,

    /// <summary>Wall-clock duration of the batch run.  Used to verify the AC-4 10-minute limit.</summary>
    TimeSpan Duration,

    /// <summary>
    /// <c>true</c> when all appointments in the window were processed without interruption.
    /// <c>false</c> when the batch was cancelled mid-run or exceeded the time budget.
    /// </summary>
    bool CompletedFullBatch);
