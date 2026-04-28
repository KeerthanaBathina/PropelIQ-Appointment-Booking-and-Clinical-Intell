namespace UPACIP.Service.Caching;

/// <summary>
/// Single point of cache invalidation for appointment state changes
/// (US_084 task_002, AC-4).
///
/// <para>
/// Centralises slot-availability and patient-profile cache eviction so
/// booking, cancellation, and reschedule flows can trigger both invalidations
/// through a single call.  All operations are fire-and-forget with error
/// swallowing — cache invalidation failures must never block the booking
/// response (edge case 2).
/// </para>
///
/// <para>
/// Concurrency note: cache is an optimisation layer only.  Database-level
/// optimistic locking (<c>Appointment.Version</c> concurrency token, US_018)
/// is the authoritative mechanism for slot contention during high-frequency
/// bookings.
/// </para>
/// </summary>
public interface ICacheInvalidationCoordinator
{
    /// <summary>
    /// Invalidates the slot availability cache entry for the booked
    /// provider/date combination AND the patient's profile cache
    /// (appointment count changed).
    ///
    /// Must be called immediately AFTER the database transaction commits —
    /// never inside the transaction — to prevent invalidating an entry that
    /// may be rolled back (AC-4).
    /// </summary>
    Task InvalidateOnBookingAsync(
        Guid?             providerId,
        DateTime          appointmentDate,
        Guid              patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the slot availability cache entry for the released
    /// provider/date combination AND the patient's profile cache.
    ///
    /// Called after a successful cancellation commit so the freed slot
    /// appears as available on the next query rather than waiting for the
    /// 5-minute TTL to expire (AC-4).
    /// </summary>
    Task InvalidateOnCancellationAsync(
        Guid?             providerId,
        DateTime          appointmentDate,
        Guid              patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates the slot cache for BOTH the old and new appointment dates
    /// (the released slot and the newly booked slot) plus the patient's profile
    /// cache — handles the rescheduling edge case where two distinct dates are
    /// affected (AC-4, edge case 2).
    /// </summary>
    Task InvalidateOnRescheduleAsync(
        Guid?             providerId,
        DateTime          oldDate,
        DateTime          newDate,
        Guid              patientId,
        CancellationToken cancellationToken = default);
}
