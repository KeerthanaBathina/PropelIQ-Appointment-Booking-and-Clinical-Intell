namespace UPACIP.DataAccess.Enums;

public enum NotificationStatus
{
    Sent,
    Failed,
    Bounced,
    /// <summary>
    /// The notification was not dispatched because the patient has opted out of
    /// this delivery channel (US_033 AC-2 / EC-2).  This is a first-class outcome
    /// — it does NOT count as a delivery failure.
    /// </summary>
    OptedOut,
    /// <summary>
    /// The appointment was cancelled between batch selection and channel send time
    /// (US_035 AC-5).  The reminder was intentionally skipped and this is logged as a
    /// first-class outcome — it does NOT count as a delivery failure.
    /// Requires migration: task_003_db_reminder_checkpoint_and_notification_status_support.
    /// </summary>
    CancelledBeforeSend,
    /// <summary>
    /// All orchestration-level retries (1 minute, 5 minutes, 15 minutes) have been
    /// exhausted without a successful delivery.  The notification is permanently
    /// undeliverable and the row is flagged for staff review (US_037 AC-3).
    /// </summary>
    PermanentlyFailed,
}
