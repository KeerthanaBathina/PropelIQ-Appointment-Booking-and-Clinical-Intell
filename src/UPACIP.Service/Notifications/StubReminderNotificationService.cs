namespace UPACIP.Service.Notifications;

/// <summary>
/// Stub implementation of <see cref="IReminderNotificationService"/> used until
/// <c>task_002_be_reminder_notification_dispatch_and_skip_handling</c> delivers the
/// real per-appointment dispatch logic.
///
/// Always returns a skipped result so the batch scheduler still exercises the full
/// checkpoint and metrics path without sending live notifications.
/// </summary>
public sealed class StubReminderNotificationService : IReminderNotificationService
{
    /// <inheritdoc/>
    public Task<ReminderNotificationResult> SendReminderAsync(
        ReminderNotificationRequest request,
        CancellationToken cancellationToken = default)
        => Task.FromResult(new ReminderNotificationResult(
            EmailSent: false,
            SmsSent: false,
            SmsSkippedOptOut: false,
            SmsSkippedChannel: true,
            FailureReason: "Reminder notification service not yet configured (stub)."));
}
