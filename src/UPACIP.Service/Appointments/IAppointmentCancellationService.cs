namespace UPACIP.Service.Appointments;

/// <summary>
/// Service contract for the appointment cancellation flow (US_019).
///
/// Responsibilities:
///   - Enforce patient ownership check (OWASP A01 IDOR prevention).
///   - Apply the UTC 24-hour cancellation cutoff (AC-2, EC-2).
///   - Return idempotent responses for already-cancelled appointments (EC-1).
///   - Persist status mutation and audit log atomically (AC-1, AC-4).
///   - Invalidate the slot availability cache so released slots appear within 1 minute (AC-3).
///
/// Implementation: <see cref="AppointmentCancellationService"/>.
/// </summary>
public interface IAppointmentCancellationService
{
    /// <summary>
    /// Cancels the specified appointment on behalf of the authenticated patient.
    ///
    /// Preconditions enforced:
    ///   1. Appointment must exist and be owned by the patient resolved from
    ///      <paramref name="userEmail"/> (OWASP A01).
    ///   2. Appointment status must be <c>Scheduled</c>; already-cancelled returns an
    ///      idempotent <see cref="CancellationResultStatus.AlreadyCancelled"/> result (EC-1).
    ///   3. <c>AppointmentTime - DateTime.UtcNow</c> must exceed 24 hours;
    ///      requests within the window return <see cref="CancellationResultStatus.PolicyBlocked"/>
    ///      with the exact required message (AC-2).
    ///
    /// On success (AC-1, AC-3, AC-4):
    ///   - Appointment status updated to <c>Cancelled</c> with UTC <c>UpdatedAt</c>.
    ///   - Immutable audit log entry persisted in the same DB transaction (AC-4).
    ///   - Slot availability cache invalidated immediately after commit (AC-3).
    ///   - Structured log written via Serilog (NFR-035).
    /// </summary>
    /// <param name="appointmentId">UUID of the appointment to cancel.</param>
    /// <param name="userEmail">
    /// Email of the authenticated patient extracted from the JWT "email" claim.
    /// Used to resolve PatientId — NEVER trusted from the request body.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="CancellationResult"/> discriminated union representing the outcome.</returns>
    Task<CancellationResult> CancelAppointmentAsync(
        Guid              appointmentId,
        string            userEmail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all appointments for the authenticated patient, ordered most-recent first.
    ///
    /// The <see cref="PatientAppointmentSummary.Cancellable"/> flag is evaluated in UTC
    /// server-side so the frontend never needs to re-compute the 24-hour rule (EC-2).
    /// </summary>
    /// <param name="userEmail">Email of the authenticated patient from the JWT claim.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Appointment list, or an empty list when the patient has no appointments.</returns>
    Task<IReadOnlyList<PatientAppointmentSummary>> GetPatientAppointmentsAsync(
        string            userEmail,
        CancellationToken cancellationToken = default);
}
