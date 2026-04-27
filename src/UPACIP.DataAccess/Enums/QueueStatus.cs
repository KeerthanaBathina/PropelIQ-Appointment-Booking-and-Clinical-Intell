namespace UPACIP.DataAccess.Enums;

public enum QueueStatus
{
    Waiting,
    InVisit,
    Completed,
    /// <summary>Automatically marked as no-show by background detection (US_052 AC-2).</summary>
    NoShow,
    /// <summary>Patient arrived after the no-show window — overridden by staff (US_052 edge case).</summary>
    ArrivedLate,
    /// <summary>Staff explicitly cancelled the queue slot (US_052 AC-3).</summary>
    Cancelled,
}
