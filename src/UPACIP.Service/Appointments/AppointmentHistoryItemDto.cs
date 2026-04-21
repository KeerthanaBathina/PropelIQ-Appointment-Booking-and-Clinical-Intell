namespace UPACIP.Service.Appointments;

/// <summary>
/// Lightweight DTO for a single appointment row in the patient's history table (US_024, FR-024).
///
/// <para>
/// Projected directly from the EF Core query — no post-processing needed.
/// All fields match the SCR-007 column set: date, time, provider, type, and status.
/// </para>
///
/// <para>
/// <strong>Timezone semantics:</strong>
/// <see cref="AppointmentTime"/> is stored and returned in UTC.
/// The frontend converts it to the patient's local timezone for display (UXR-401).
/// </para>
/// </summary>
/// <param name="Id">Appointment UUID.</param>
/// <param name="BookingReference">Human-readable booking reference (e.g. BK-20260421-X7R2KP).</param>
/// <param name="AppointmentTime">UTC appointment start time.</param>
/// <param name="ProviderName">Display name of the assigned provider.</param>
/// <param name="AppointmentType">Category of the appointment (e.g. "General Checkup").</param>
/// <param name="Status">
/// Current lifecycle state as a string matching the frontend <c>AppointmentStatus</c> union
/// (Scheduled | Completed | Cancelled | NoShow).
/// Cancelled appointments are included per EC-2.
/// </param>
public sealed record AppointmentHistoryItemDto(
    Guid     Id,
    string   BookingReference,
    DateTime AppointmentTime,
    string   ProviderName,
    string   AppointmentType,
    string   Status);
