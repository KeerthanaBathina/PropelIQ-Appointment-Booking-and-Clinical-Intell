namespace UPACIP.Service.Validation;

/// <summary>
/// Request DTO for creating a new appointment.
/// Used by <see cref="AppointmentDateValidator"/> and <see cref="SoftDeleteReferenceValidator"/>.
/// Controller-specific fields (notes, preferences) are added when the Appointment controller
/// is implemented; this DTO covers the fields required for validation rules DR-012 and AC-3.
/// </summary>
public sealed record CreateAppointmentRequest
{
    /// <summary>ID of the patient for whom the appointment is being booked.</summary>
    public Guid PatientId { get; init; }

    /// <summary>
    /// Proposed appointment date and time (UTC).
    /// Must be between now and 90 days in the future (DR-012).
    /// </summary>
    public DateTimeOffset AppointmentTime { get; init; }
}
