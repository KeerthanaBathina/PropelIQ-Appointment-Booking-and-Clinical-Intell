namespace UPACIP.Service.Appointments;

/// <summary>
/// Service contract for the appointment booking flow (US_018).
/// Implementation: <see cref="AppointmentBookingService"/>.
/// </summary>
public interface IAppointmentBookingService
{
    /// <summary>
    /// Books an appointment for the authenticated user identified by <paramref name="userEmail"/>.
    ///
    /// Pre-conditions enforced:
    ///   1. A valid Redis hold must exist for the slot and belong to the resolved patient (AC-3).
    ///   2. The slot must still be available (no non-cancelled appointment at the same provider/time).
    ///   3. AppointmentTime must be within the 90-day advance-booking window (FR-013, EC-2).
    ///
    /// Concurrency (FR-012, TR-015):
    ///   On <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> or DB
    ///   unique-constraint violation (PostgreSQL 23505), returns
    ///   <see cref="BookingResult.Conflicted"/> with up to 3 alternative available slots (AC-2).
    ///
    /// Resilience (EC-1, NFR-032):
    ///   Polly single-retry (500 ms delay) for transient Npgsql failures.
    ///   On retry exhaustion returns <see cref="BookingResult.Unavailable"/>.
    ///
    /// On success (AC-1):
    ///   - Appointment persisted with status "Scheduled" and Version = 0.
    ///   - Unique booking reference generated (format: BK-{YYYYMMDD}-{6-char}).
    ///   - Redis slot cache invalidated via <see cref="IAppointmentSlotService.InvalidateCacheAsync"/>.
    ///   - Slot hold released.
    ///   - Success event logged (NFR-035).
    /// </summary>
    /// <param name="request">Validated booking request (SlotId, ProviderId, AppointmentTime, AppointmentType).</param>
    /// <param name="userEmail">Email of the authenticated user extracted from the JWT "email" claim.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="BookingResult"/> discriminated union representing the outcome.</returns>
    Task<BookingResult> BookAppointmentAsync(
        BookingRequest    request,
        string            userEmail,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically moves an existing <paramref name="appointmentId"/> to <paramref name="newSlot"/>
    /// using EF Core optimistic locking (Version concurrency token).
    ///
    /// Used exclusively by the preferred-slot swap engine (US_021 AC-1, EC-1).
    /// The caller is responsible for:
    ///   - Verifying the appointment is eligible (status = Scheduled, not checked in).
    ///   - Confirming the new slot is within 24 hours if manual confirmation is needed.
    ///
    /// On <see cref="Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException"/> the method
    /// returns <see cref="BookingResult.Conflicted"/> so the swap processor can retry the next candidate.
    /// </summary>
    Task<RescheduleResult> RescheduleAppointmentAsync(
        Guid              appointmentId,
        SlotItem          newSlot,
        CancellationToken cancellationToken = default);
}
