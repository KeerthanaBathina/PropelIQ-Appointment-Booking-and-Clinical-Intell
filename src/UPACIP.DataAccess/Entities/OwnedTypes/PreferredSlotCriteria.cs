namespace UPACIP.DataAccess.Entities.OwnedTypes;

/// <summary>
/// Owned type stored as a JSONB column in the appointments table.
/// Captures patient-stated scheduling preferences used by the AI scheduler.
/// </summary>
public sealed class PreferredSlotCriteria
{
    /// <summary>Preferred day-of-week values (e.g. "Monday", "Friday").</summary>
    public List<string> PreferredDays { get; set; } = [];

    /// <summary>Preferred time-of-day window (e.g. "morning", "afternoon").</summary>
    public string? PreferredTimeOfDay { get; set; }

    /// <summary>Maximum acceptable wait time in minutes.</summary>
    public int? MaxWaitMinutes { get; set; }

    /// <summary>Any free-text additional notes provided by the patient.</summary>
    public string? Notes { get; set; }
}
