namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Defines when a provider is available for bookings on a given day of the week (US_017).
///
/// The slot availability service (<c>AppointmentSlotService</c>) reads active templates
/// to generate the bookable 30-minute time-slot grid for each provider. Templates describe
/// <em>recurring weekly availability</em> — they are not modified when appointments are booked.
///
/// A unique constraint on <c>(ProviderId, DayOfWeek, StartTime)</c> prevents overlapping
/// templates for the same provider on the same day. Check constraints enforce
/// <c>SlotDurationMinutes > 0</c> and <c>EndTime > StartTime</c>.
/// </summary>
public sealed class ProviderAvailabilityTemplate : BaseEntity
{
    /// <summary>
    /// FK to <see cref="ApplicationUser"/> where the user holds the Staff role.
    /// The provider's display name is stored separately for denormalised fast access.
    /// </summary>
    public Guid ProviderId { get; set; }

    /// <summary>Full display name of the provider (max 100 chars). Denormalised for query speed.</summary>
    public string ProviderName { get; set; } = string.Empty;

    /// <summary>
    /// Day of the week this template applies to (0 = Sunday, 6 = Saturday).
    /// Matches <see cref="DayOfWeek"/> enum values.
    /// </summary>
    public int DayOfWeek { get; set; }

    /// <summary>
    /// Time of day when availability begins (provider local / clinic UTC time).
    /// Must be before <see cref="EndTime"/> (enforced by check constraint).
    /// </summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>
    /// Time of day when availability ends.
    /// Must be after <see cref="StartTime"/> (enforced by check constraint).
    /// </summary>
    public TimeOnly EndTime { get; set; }

    /// <summary>
    /// Duration of each generated slot in minutes.
    /// Default 30. Must be positive (enforced by check constraint).
    /// </summary>
    public int SlotDurationMinutes { get; set; } = 30;

    /// <summary>
    /// Appointment type offered during this availability window
    /// (e.g. "General Checkup", "Follow-up", "Consultation"). Max 50 chars.
    /// </summary>
    public string AppointmentType { get; set; } = "General Checkup";

    /// <summary>
    /// Whether this template is currently in effect.
    /// Inactive templates are excluded from slot generation without requiring a delete.
    /// </summary>
    public bool IsActive { get; set; } = true;

    // -------------------------------------------------------------------------
    // Navigation property
    // -------------------------------------------------------------------------

    /// <summary>The staff user this template is associated with.</summary>
    public ApplicationUser Provider { get; set; } = null!;
}
