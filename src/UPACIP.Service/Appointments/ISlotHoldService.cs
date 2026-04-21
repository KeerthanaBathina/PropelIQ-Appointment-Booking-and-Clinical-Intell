namespace UPACIP.Service.Appointments;

/// <summary>
/// Redis-backed temporary slot hold service for the appointment booking flow (US_018, AC-3).
///
/// Redis key pattern : <c>hold:{slotId}</c>
/// Value             : user email (normalised to lower-case)
/// TTL               : 60 seconds (auto-expires per AC-3)
///
/// Using the user's email as the hold value decouples the hold mechanism from the
/// Patient database table, avoiding a DB round-trip on every hold operation while
/// still providing per-user isolation. The authoritative concurrency guard is the
/// database-level uniqueness check in <see cref="AppointmentBookingService"/> combined
/// with EF Core optimistic concurrency (<see cref="UPACIP.DataAccess.Entities.Appointment.Version"/>).
/// </summary>
public interface ISlotHoldService
{
    /// <summary>
    /// Attempts to acquire a 60-second hold on <paramref name="slotId"/> for <paramref name="userEmail"/>.
    ///
    /// Returns <c>true</c> when the hold is granted or the same user already owns it (extends TTL).
    /// Returns <c>false</c> when the slot is held by a <em>different</em> user (AC-3).
    /// </summary>
    /// <param name="slotId">Stable slot identifier (format: {yyyyMMdd}-{HHmm}-{providerGuid:N}).</param>
    /// <param name="userEmail">Normalised email of the authenticated user from the JWT claim.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> AcquireHoldAsync(string slotId, string userEmail, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases the hold on <paramref name="slotId"/> when owned by <paramref name="userEmail"/>.
    /// No-op when no matching hold exists or when the hold belongs to a different user (idempotent).
    /// </summary>
    Task ReleaseHoldAsync(string slotId, string userEmail, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <c>true</c> when a valid (non-expired) hold for <paramref name="slotId"/>
    /// exists and belongs to <paramref name="userEmail"/>.
    /// </summary>
    Task<bool> IsHeldByUserAsync(string slotId, string userEmail, CancellationToken cancellationToken = default);
}
