using System.ComponentModel.DataAnnotations;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Request payload for patient-initiated appointment rescheduling (US_023 FR-023).
///
/// PatientId is NOT accepted from the client — it is resolved server-side from the
/// authenticated user's JWT email claim to prevent IDOR attacks (OWASP A01).
///
/// Validation: DataAnnotations enforce the model-binding boundary.
/// Business rules (24-hour cutoff, walk-in restriction) are enforced in
/// <see cref="AppointmentReschedulingService"/>.
/// </summary>
public sealed record RescheduleAppointmentRequest
{
    /// <summary>
    /// Stable slot identifier for the replacement slot.
    /// Format: {yyyyMMdd}-{HHmm}-{providerGuid:N}.
    /// Must identify an available, future slot within the 90-day booking window.
    /// </summary>
    [Required(ErrorMessage = "SlotId is required.")]
    [StringLength(50, ErrorMessage = "SlotId must not exceed 50 characters.")]
    public string SlotId { get; init; } = string.Empty;

    /// <summary>Provider owning the replacement slot.</summary>
    [Required(ErrorMessage = "ProviderId is required.")]
    public Guid ProviderId { get; init; }

    /// <summary>
    /// UTC date and time of the replacement slot.
    /// Must be &gt; now and ≤ today + 90 days (FR-013).
    /// </summary>
    [Required(ErrorMessage = "NewAppointmentTime is required.")]
    public DateTime NewAppointmentTime { get; init; }

    /// <summary>
    /// Provider full display name — carried through from slot selection so the confirmation
    /// response can return it without an additional DB lookup.
    /// Max 100 characters.
    /// </summary>
    [Required(ErrorMessage = "ProviderName is required.")]
    [StringLength(100, ErrorMessage = "ProviderName must not exceed 100 characters.")]
    public string ProviderName { get; init; } = string.Empty;

    /// <summary>
    /// Appointment type (e.g. "General Checkup", "Follow-up").
    /// Must match the original appointment's type — rescheduling does not change the visit category.
    /// Max 50 characters.
    /// </summary>
    [Required(ErrorMessage = "AppointmentType is required.")]
    [StringLength(50, ErrorMessage = "AppointmentType must not exceed 50 characters.")]
    public string AppointmentType { get; init; } = string.Empty;
}
