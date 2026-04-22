namespace UPACIP.Service.Notifications;

/// <summary>
/// Contract for per-appointment reminder dispatch invoked by
/// <see cref="IReminderBatchSchedulerService"/> during a scheduled reminder run (US_035).
///
/// Channel selection is determined by <see cref="ReminderNotificationRequest.BatchType"/>:
/// <list type="bullet">
///   <item><see cref="ReminderBatchType.TwentyFourHour"/> → email + SMS (AC-1)</item>
///   <item><see cref="ReminderBatchType.TwoHour"/> → SMS only (AC-2)</item>
/// </list>
///
/// Implementations must never throw.  All delivery outcomes (including channel-level
/// failures and patient opt-outs) are encoded in <see cref="ReminderNotificationResult"/>
/// so the batch scheduler can continue processing the next appointment without
/// needing exception handling.
///
/// <para>Note: the production implementation is provided by
/// <c>task_002_be_reminder_notification_dispatch_and_skip_handling</c>. Until that
/// task completes, a stub implementation registers here that always returns a
/// no-op skip result.</para>
/// </summary>
public interface IReminderNotificationService
{
    /// <summary>
    /// Dispatches a reminder notification for the appointment described by
    /// <paramref name="request"/> on the channels appropriate for the batch type.
    /// </summary>
    /// <param name="request">Appointment context for the reminder notification.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="ReminderNotificationResult"/> summarising what was sent.
    /// Never throws.
    /// </returns>
    Task<ReminderNotificationResult> SendReminderAsync(
        ReminderNotificationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Appointment-context payload supplied to
/// <see cref="IReminderNotificationService.SendReminderAsync"/>.
/// </summary>
public sealed record ReminderNotificationRequest(
    Guid AppointmentId,
    Guid PatientId,
    string PatientEmail,
    string PatientPhoneNumber,
    string PatientFullName,
    DateTime AppointmentTimeUtc,
    string? ProviderName,
    string? AppointmentType,
    string? BookingReference,
    ReminderBatchType BatchType,
    string? CorrelationId = null);

/// <summary>
/// Outcome returned by <see cref="IReminderNotificationService.SendReminderAsync"/>.
/// </summary>
/// <param name="EmailSent"><c>true</c> when the email was accepted by an SMTP provider.</param>
/// <param name="SmsSent"><c>true</c> when the SMS was accepted by Twilio.</param>
/// <param name="SmsSkippedOptOut">
/// <c>true</c> when the SMS was skipped because the patient opted out of SMS
/// notifications.  Does NOT count as a failure.
/// </param>
/// <param name="SmsSkippedChannel">
/// <c>true</c> when this batch type does not send SMS (currently unused — 2h sends SMS only).
/// </param>
/// <param name="FailureReason">
/// Non-null when neither email nor SMS succeeded.  Used for structured log output by
/// the batch scheduler.
/// </param>
public sealed record ReminderNotificationResult(
    bool EmailSent,
    bool SmsSent,
    bool SmsSkippedOptOut,
    bool SmsSkippedChannel = false,
    string? FailureReason = null)
{
    /// <summary>
    /// <c>true</c> when at least one channel accepted the notification or the skip
    /// was an expected opt-out.  The batch scheduler counts this as a processed record.
    /// </summary>
    public bool CountsAsProcessed =>
        EmailSent || SmsSent || SmsSkippedOptOut || SmsSkippedChannel;
}
