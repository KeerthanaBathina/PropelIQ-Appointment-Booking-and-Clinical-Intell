namespace UPACIP.DataAccess.Enums;

public enum NotificationType
{
    Confirmation,
    Reminder24h,
    Reminder2h,
    /// <summary>Automatic slot-swap completion — old and new appointment times included (US_021 AC-2).</summary>
    SlotSwapCompleted,
    /// <summary>Manual-confirmation offer for a preferred slot inside the 24-hour window (US_021 AC-5).</summary>
    SlotSwapManualConfirmation,
    /// <summary>Waitlist offer notification — a matching slot became available (US_020 AC-2).</summary>
    WaitlistOffer,
}
