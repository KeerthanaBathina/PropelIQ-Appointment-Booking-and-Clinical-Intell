namespace UPACIP.Service.Appointments;

/// <summary>
/// Service contract for patient-initiated atomic appointment rescheduling (US_023 FR-023).
///
/// Responsibilities:
///   1. Verify patient ownership from the JWT email claim (OWASP A01 — IDOR prevention).
///   2. Reject walk-in appointments with a clear restriction message (EC-2).
///   3. Enforce the 24-hour reschedule cutoff against the ORIGINAL appointment time in UTC (AC-2).
///   4. Delegate the atomic slot swap to <see cref="IAppointmentBookingService.RescheduleAppointmentAsync"/>
///      which manages EF Core optimistic locking and cache invalidation.
///   5. Write an immutable audit log entry on both success and policy rejection.
///   6. Trigger downstream notification workflows (email/SMS, calendar sync) after a
///      successful reschedule (AC-4).
///
/// Implementation: <see cref="AppointmentReschedulingService"/>.
/// </summary>
public interface IAppointmentReschedulingService
{
    /// <summary>
    /// Atomically reschedules an existing appointment to the specified replacement slot.
    ///
    /// The caller supplies the <paramref name="userEmail"/> extracted from the JWT.
    /// The patient identity is NEVER inferred from the request body (OWASP A01).
    ///
    /// Outcome rules:
    ///   - <see cref="RescheduleAppointmentStatus.Succeeded"/>: appointment updated; both old
    ///     and new times available in the result for confirmation display (AC-3).
    ///   - <see cref="RescheduleAppointmentStatus.PolicyBlocked"/> (HTTP 422): reschedule
    ///     attempted within 24 hours of the original appointment time (AC-2).
    ///   - <see cref="RescheduleAppointmentStatus.WalkInRestricted"/> (HTTP 403): appointment
    ///     is a walk-in and may not be rescheduled by the patient (EC-2).
    ///   - <see cref="RescheduleAppointmentStatus.SlotUnavailable"/> (HTTP 409): selected
    ///     replacement slot was taken by another booking during confirmation (EC-1).
    ///   - <see cref="RescheduleAppointmentStatus.NotFound"/> (HTTP 404): appointment does not
    ///     exist or does not belong to the requesting patient.
    ///
    /// Timezone (AC-2, EC-2):
    ///   All time comparisons are performed in UTC. Appointment times are stored in UTC by the
    ///   booking service; no client-side timezone conversion is accepted.
    /// </summary>
    /// <param name="appointmentId">UUID of the appointment to reschedule.</param>
    /// <param name="request">Replacement slot details (SlotId, ProviderId, NewAppointmentTime, AppointmentType).</param>
    /// <param name="userEmail">Email of the authenticated patient extracted from the JWT.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="RescheduleAppointmentResult"/> describing the outcome.
    /// </returns>
    Task<RescheduleAppointmentResult> RescheduleAppointmentAsync(
        Guid                       appointmentId,
        RescheduleAppointmentRequest request,
        string                     userEmail,
        CancellationToken          cancellationToken = default);
}
