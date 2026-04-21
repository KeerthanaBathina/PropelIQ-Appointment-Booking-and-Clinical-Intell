using System.ComponentModel.DataAnnotations;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Request model for booking an appointment (POST /api/appointments, US_018).
///
/// PatientId is NOT provided by the client — it is resolved server-side from the
/// authenticated user's JWT email claim to prevent IDOR attacks (OWASP A01).
///
/// Validation: <see cref="UPACIP.Service.Validation.BookingRequestValidator"/> (FluentValidation).
/// DataAnnotations on this record are a secondary line of defence for model-binding failures.
/// </summary>
public sealed record BookingRequest
{
    /// <summary>
    /// Stable slot identifier from <see cref="SlotItem.SlotId"/>.
    /// Format: {yyyyMMdd}-{HHmm}-{providerGuid:N}.
    /// Required to look up the Redis hold key for verification (AC-3).
    /// </summary>
    [Required(ErrorMessage = "SlotId is required.")]
    [StringLength(50, ErrorMessage = "SlotId must not exceed 50 characters.")]
    public string SlotId { get; init; } = string.Empty;

    /// <summary>Provider whose availability slot is being booked (FR-012).</summary>
    [Required(ErrorMessage = "ProviderId is required.")]
    public Guid ProviderId { get; init; }

    /// <summary>
    /// UTC date and time of the appointment.
    /// Must be &gt; now and ≤ today + 90 days (FR-013, EC-2).
    /// FluentValidation enforces this; DataAnnotations [Required] guards model binding.
    /// </summary>
    [Required(ErrorMessage = "AppointmentTime is required.")]
    public DateTime AppointmentTime { get; init; }

    /// <summary>
    /// Appointment type label (e.g. "General Checkup", "Follow-up"). Max 50 characters.
    /// Must match an active <c>ProviderAvailabilityTemplate.AppointmentType</c> for the provider.
    /// </summary>
    [Required(ErrorMessage = "AppointmentType is required.")]
    [StringLength(50, ErrorMessage = "AppointmentType must not exceed 50 characters.")]
    public string AppointmentType { get; init; } = string.Empty;
}
