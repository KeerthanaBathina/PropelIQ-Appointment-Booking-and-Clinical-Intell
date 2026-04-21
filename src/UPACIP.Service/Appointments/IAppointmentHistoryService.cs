namespace UPACIP.Service.Appointments;

/// <summary>
/// Service contract for the patient appointment history endpoint (US_024, FR-024).
///
/// Responsibilities:
///   - Enforce patient ownership: resolve PatientId from JWT email, never from the request body (OWASP A01).
///   - Return ALL statuses (Scheduled, Completed, Cancelled, NoShow) so cancelled rows remain visible (EC-2).
///   - Return 200 with an empty result set when the patient has no appointments (EC-1).
///   - Apply newest-first ordering by default (AC-1) with optional ascending toggle (AC-2).
///   - Enforce fixed page size of 10 and return stable pagination metadata (AC-3).
///   - Use lightweight EF Core projection so only required columns are fetched from the DB (NFR-004).
///
/// Implementation: <see cref="AppointmentHistoryService"/>.
/// </summary>
public interface IAppointmentHistoryService
{
    /// <summary>
    /// Returns a paginated, sorted appointment history for the authenticated patient.
    ///
    /// <para>
    /// <strong>Ownership (OWASP A01):</strong>
    /// <paramref name="userEmail"/> is resolved from the JWT email claim by the controller.
    /// Patient identity is NEVER trusted from the query string or request body.
    /// </para>
    ///
    /// <para>
    /// <strong>Empty state (EC-1):</strong>
    /// Returns an <see cref="AppointmentHistoryResponse"/> with an empty <c>Items</c> list
    /// and zero counts rather than throwing or returning null.
    /// </para>
    ///
    /// <para>
    /// <strong>Cancelled rows (EC-2):</strong>
    /// Cancelled and no-show appointments are included in results — they are never filtered out.
    /// </para>
    /// </summary>
    /// <param name="userEmail">Email of the authenticated patient from the JWT claim.</param>
    /// <param name="query">Pagination and sort parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A populated or empty <see cref="AppointmentHistoryResponse"/> with accurate pagination metadata.
    /// </returns>
    Task<AppointmentHistoryResponse> GetHistoryAsync(
        string                   userEmail,
        AppointmentHistoryQuery  query,
        CancellationToken        cancellationToken = default);
}
