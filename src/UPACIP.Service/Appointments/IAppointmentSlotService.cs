namespace UPACIP.Service.Appointments;

/// <summary>
/// Service contract for appointment slot availability queries (US_017).
///
/// Implementation: <see cref="AppointmentSlotService"/>.
/// Cache layer: <see cref="AppointmentSlotCacheService"/>.
/// </summary>
public interface IAppointmentSlotService
{
    /// <summary>
    /// Returns available 30-minute appointment slots for the given date range and optional filters.
    ///
    /// Cache strategy (AC-4, NFR-030):
    ///   - Hit: returns cached <see cref="SlotAvailabilityResponse"/> from Redis (sub-second).
    ///   - Miss: queries PostgreSQL, populates cache with 5-minute TTL, returns response.
    /// </summary>
    /// <param name="parameters">Validated query parameters (date range, provider, type).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SlotAvailabilityResponse> GetAvailableSlotsAsync(
        SlotQueryParameters parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes cached slot entries for the specified date so stale availability is not served
    /// after an appointment is booked or cancelled.
    ///
    /// Called by booking/cancellation flows to maintain cache consistency.
    /// </summary>
    /// <param name="date">The appointment date whose cache entries should be invalidated.</param>
    /// <param name="providerId">Optional provider scope; null invalidates all provider entries for the date.</param>
    Task InvalidateCacheAsync(
        DateOnly date,
        Guid? providerId = null,
        CancellationToken cancellationToken = default);
}
