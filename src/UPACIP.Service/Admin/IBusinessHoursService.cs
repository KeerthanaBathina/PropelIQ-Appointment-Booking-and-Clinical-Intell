namespace UPACIP.Service.Admin;

/// <summary>
/// Business logic for clinic-wide business hours and holiday management (US_059 AC-3, AC-4).
///
/// Implementations must:
///   - Cache business hours in Redis under <c>config:hours</c> (5-min TTL).
///   - Cache holidays in Redis under <c>config:holidays</c> (5-min TTL).
///   - Invalidate the relevant cache keys on every write.
///   - Append an AuditLog entry for every write (NFR-012, NFR-035).
/// </summary>
public interface IBusinessHoursService
{
    /// <summary>
    /// Returns the operating hours for all 7 days of the week.
    /// Reads from Redis cache (5-min TTL) with DB fallback.
    /// </summary>
    Task<IReadOnlyList<BusinessHoursEntryDto>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Bulk-replaces the operating hours for the provided days.
    /// Partial updates are supported — entries not included in the request are left unchanged.
    /// </summary>
    Task<IReadOnlyList<BusinessHoursEntryDto>> UpdateAllAsync(
        UpdateBusinessHoursRequest request,
        Guid                       adminUserId,
        CancellationToken          ct = default);

    /// <summary>
    /// Returns all active (non-deleted) holiday definitions, ordered by date.
    /// Reads from Redis cache (5-min TTL) with DB fallback.
    /// </summary>
    Task<IReadOnlyList<HolidayResponse>> GetHolidaysAsync(CancellationToken ct = default);

    /// <summary>
    /// Creates a new holiday definition.
    ///
    /// <para>
    /// After persisting the holiday the method queries for non-cancelled appointments
    /// scheduled on <see cref="CreateHolidayRequest.Date"/> and returns them in
    /// <see cref="AddHolidayResponse.AffectedAppointments"/> so the admin can review
    /// and take follow-up action (AC-4 edge case).
    /// </para>
    /// </summary>
    Task<AddHolidayResponse> AddHolidayAsync(
        CreateHolidayRequest request,
        Guid                 adminUserId,
        CancellationToken    ct = default);

    /// <summary>
    /// Soft-deletes the holiday with the given id by setting its <c>DeletedAt</c> timestamp.
    /// Returns <c>false</c> when the holiday is not found or is already deleted.
    /// </summary>
    Task<bool> RemoveHolidayAsync(
        Guid              holidayId,
        Guid              adminUserId,
        CancellationToken ct = default);
}
