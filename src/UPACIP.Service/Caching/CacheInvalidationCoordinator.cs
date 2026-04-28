using Microsoft.Extensions.Logging;
using UPACIP.Service.Appointments;

namespace UPACIP.Service.Caching;

/// <summary>
/// Centralized cache invalidation orchestrator for appointment state changes
/// (US_084 task_002, AC-4, edge case 2).
///
/// <para>
/// Coordinates eviction of both slot-availability and patient-profile cache
/// entries so that a single coordinator call from the booking/cancellation
/// flow satisfies all invalidation requirements.  All cache operations are
/// delegated to <see cref="IAppointmentSlotService.InvalidateCacheAsync"/>
/// and <see cref="PatientProfileCacheService.InvalidateProfileAsync"/>
/// which are individually fault-tolerant.
/// </para>
///
/// <para>
/// <strong>Design invariant</strong>: cache invalidation never blocks the
/// caller response.  Any exception is caught, logged, and swallowed so that
/// a Redis outage cannot cause a booking or cancellation to fail (edge case 2).
/// </para>
///
/// <para>
/// <strong>Concurrent booking resolution</strong>: this coordinator is an
/// optimisation layer only.  When two users simultaneously view the same
/// available slot and both attempt to book:
/// <list type="number">
///   <item>The first booking succeeds and this coordinator evicts the cache.</item>
///   <item>The second booking attempt fails with
///     <c>DbUpdateConcurrencyException</c> / PostgreSQL 23505 unique violation
///     — the booking API returns HTTP 409 with 3 alternative slots fetched
///     directly from the database (not cache).</item>
/// </list>
/// Database-level optimistic locking (<c>Appointment.Version</c>, US_018)
/// is the authoritative contention mechanism; cache consistency is eventual.
/// </para>
/// </summary>
public sealed class CacheInvalidationCoordinator : ICacheInvalidationCoordinator
{
    private readonly IAppointmentSlotService             _slotService;
    private readonly PatientProfileCacheService          _profileCache;
    private readonly ILogger<CacheInvalidationCoordinator> _logger;

    public CacheInvalidationCoordinator(
        IAppointmentSlotService              slotService,
        PatientProfileCacheService           profileCacheService,
        ILogger<CacheInvalidationCoordinator> logger)
    {
        _slotService  = slotService;
        _profileCache = profileCacheService;
        _logger       = logger;
    }

    // ── ICacheInvalidationCoordinator ─────────────────────────────────────────

    /// <inheritdoc />
    public async Task InvalidateOnBookingAsync(
        Guid?             providerId,
        DateTime          appointmentDate,
        Guid              patientId,
        CancellationToken cancellationToken = default)
    {
        await InvalidateCoreAsync(
            operation:       "booking",
            providerId:      providerId,
            dates:           [appointmentDate],
            patientId:       patientId,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task InvalidateOnCancellationAsync(
        Guid?             providerId,
        DateTime          appointmentDate,
        Guid              patientId,
        CancellationToken cancellationToken = default)
    {
        await InvalidateCoreAsync(
            operation:       "cancellation",
            providerId:      providerId,
            dates:           [appointmentDate],
            patientId:       patientId,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task InvalidateOnRescheduleAsync(
        Guid?             providerId,
        DateTime          oldDate,
        DateTime          newDate,
        Guid              patientId,
        CancellationToken cancellationToken = default)
    {
        // Both dates must be invalidated; they may share the same slot entry when
        // the date is the same, in which case the second call is a cheap no-op.
        await InvalidateCoreAsync(
            operation:       "reschedule",
            providerId:      providerId,
            dates:           [oldDate, newDate],
            patientId:       patientId,
            cancellationToken: cancellationToken);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task InvalidateCoreAsync(
        string            operation,
        Guid?             providerId,
        DateTime[]        dates,
        Guid              patientId,
        CancellationToken cancellationToken)
    {
        try
        {
            // Evict slot cache for each affected date (de-duplicated by key in the slot service).
            var dateSet = dates.Select(DateOnly.FromDateTime).Distinct();
            foreach (var date in dateSet)
            {
                await _slotService.InvalidateCacheAsync(date, providerId, cancellationToken);
            }

            // Evict patient profile (appointment count changed).
            await _profileCache.InvalidateProfileAsync(patientId, cancellationToken);

            _logger.LogDebug(
                "Cache invalidated on {Operation}: slots for provider={ProviderId} " +
                "dates={Dates}, patient profile={PatientId}.",
                operation,
                providerId?.ToString() ?? "all",
                string.Join(", ", dates.Select(d => d.ToString("yyyy-MM-dd"))),
                patientId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Cache invalidation failures must never block the booking/cancellation response.
            _logger.LogWarning(ex,
                "Cache invalidation on {Operation} failed for provider={ProviderId} " +
                "patient={PatientId}. Stale cache entries will expire after TTL.",
                operation,
                providerId?.ToString() ?? "all",
                patientId);
        }
    }
}
