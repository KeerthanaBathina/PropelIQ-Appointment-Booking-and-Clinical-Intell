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
}
