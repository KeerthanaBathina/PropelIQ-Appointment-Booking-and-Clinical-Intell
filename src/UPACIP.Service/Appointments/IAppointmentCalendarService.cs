namespace UPACIP.Service.Appointments;

/// <summary>
/// Service contract for generating downloadable iCalendar (.ics) payloads (US_025, FR-025, TR-026).
///
/// Ownership (OWASP A01):
///   The caller supplies the authenticated patient's email; the service resolves the
///   patient identity server-side and rejects any request whose appointment does not
///   belong to that patient, returning <c>null</c> rather than leaking a 403 that would
///   confirm the appointment exists for a different patient.
///
/// Stable UID (AC-3):
///   The iCal event UID is derived from the persistent appointment identity so that
///   regenerated files update — rather than duplicate — the existing calendar entry.
///
/// Implementation: <see cref="AppointmentCalendarService"/>.
/// </summary>
public interface IAppointmentCalendarService
{
    /// <summary>
    /// Generates a standards-compliant iCalendar payload for the specified appointment.
    ///
    /// Pre-conditions enforced:
    ///   1. The appointment must exist and belong to the patient identified by <paramref name="userEmail"/>.
    ///   2. The appointment status must be <c>Scheduled</c>; cancelled or completed appointments
    ///      are not eligible for export.
    ///
    /// Returns:
    ///   An <see cref="AppointmentCalendarDownloadResponse"/> on success, or <c>null</c> when the
    ///   appointment is not found, not owned by the patient, or not in an exportable status.
    /// </summary>
    /// <param name="appointmentId">UUID of the appointment to export.</param>
    /// <param name="userEmail">Email of the authenticated patient from the JWT claim.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    ///   <see cref="AppointmentCalendarDownloadResponse"/> on success; <c>null</c> on not-found /
    ///   ownership failure / ineligible status.
    /// </returns>
    Task<AppointmentCalendarDownloadResponse?> GetCalendarFileAsync(
        Guid              appointmentId,
        string            userEmail,
        CancellationToken cancellationToken = default);
}
