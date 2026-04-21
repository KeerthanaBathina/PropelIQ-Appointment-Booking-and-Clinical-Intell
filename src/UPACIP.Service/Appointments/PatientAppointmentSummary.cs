namespace UPACIP.Service.Appointments;

/// <summary>
/// Lightweight appointment summary returned by
/// <see cref="IAppointmentCancellationService.GetPatientAppointmentsAsync"/>
/// for the patient appointment list (GET /api/appointments, US_019).
///
/// <para>
/// <strong>Timezone semantics (EC-2):</strong>
/// <see cref="AppointmentTime"/> is stored and returned in UTC.
/// The frontend is responsible for converting it to the patient's local timezone for display.
/// The 24-hour <see cref="Cancellable"/> flag is evaluated in UTC by the backend — the client
/// MUST NOT re-compute cancellation eligibility (EC-2).
/// </para>
/// </summary>
/// <param name="Id">Appointment UUID.</param>
/// <param name="BookingReference">Human-readable booking reference (e.g. BK-20260421-X7R2KP).</param>
/// <param name="AppointmentTime">UTC appointment start time.</param>
/// <param name="ProviderName">Display name of the assigned provider.</param>
/// <param name="AppointmentType">Category of the appointment (e.g. "General Checkup").</param>
/// <param name="Status">Current lifecycle state as a string (matches frontend <c>AppointmentStatus</c> union).</param>
/// <param name="Cancellable">
/// <c>true</c> when the appointment is Scheduled and the UTC time remaining exceeds 24 hours.
/// Evaluated server-side — client MUST treat this as authoritative (EC-2).
/// </param>
public sealed record PatientAppointmentSummary(
    Guid     Id,
    string   BookingReference,
    DateTime AppointmentTime,
    string   ProviderName,
    string   AppointmentType,
    string   Status,
    bool     Cancellable);
