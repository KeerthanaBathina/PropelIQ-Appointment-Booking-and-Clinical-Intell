using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Caching;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Implements appointment slot availability queries with Redis cache-aside strategy (US_017).
///
/// Slot generation model (US_017 TASK_003):
///   - Providers and their schedules are sourced from <c>provider_availability_templates</c>
///     (active templates only). Each template defines start/end time, slot duration, and
///     appointment type for a specific provider + day-of-week combination.
///   - A slot is <em>unavailable</em> when an existing non-Cancelled appointment exists for that
///     provider at the same start time (composite index hit on appointment_time + status + provider_id).
///   - On empty systems (no templates yet), the response contains zero slots — correct behaviour.
///
/// Cache strategy (AC-4, NFR-030):
///   - Cache key pattern: <c>slots:{startDate}:{endDate}:{providerId}:{appointmentType}</c>.
///   - TTL: 5 minutes (NFR-030).
///   - Failures are swallowed by <see cref="ICacheService"/> — the DB query always runs on
///     cache failure (fallback per AC-4).
///   - Invalidation: called explicitly by booking/cancellation flows via
///     <see cref="IAppointmentSlotService.InvalidateCacheAsync"/>.
/// </summary>
public sealed class AppointmentSlotService : IAppointmentSlotService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5); // NFR-030

    private readonly ApplicationDbContext            _db;
    private readonly ICacheService                   _cache;
    private readonly ILogger<AppointmentSlotService> _logger;

    public AppointmentSlotService(
        ApplicationDbContext              db,
        ICacheService                     cache,
        ILogger<AppointmentSlotService>   logger)
    {
        _db     = db;
        _cache  = cache;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IAppointmentSlotService
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<SlotAvailabilityResponse> GetAvailableSlotsAsync(
        SlotQueryParameters parameters,
        CancellationToken   cancellationToken = default)
    {
        var cacheKey = BuildRangeCacheKey(parameters);

        // Cache-aside: attempt to serve from Redis first (AC-4, NFR-030)
        var cached = await _cache.GetAsync<SlotAvailabilityResponse>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            _logger.LogDebug(
                "Slot cache HIT for key {CacheKey}. Returning {SlotCount} slots.",
                cacheKey, cached.Slots.Count);
            return cached;
        }

        _logger.LogDebug("Slot cache MISS for key {CacheKey}. Querying database.", cacheKey);

        // ── Build date range ──────────────────────────────────────────────────
        var startDt = parameters.StartDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endDt   = parameters.ResolvedEndDate.ToDateTime(new TimeOnly(23, 59, 59), DateTimeKind.Utc);

        // ── Load active provider templates for the requested days (US_017 TASK_003) ──
        // ix_provider_availability_templates_provider_id_day_of_week is used here.
        var daysInRange = Enumerable
            .Range(0, parameters.ResolvedEndDate.DayNumber - parameters.StartDate.DayNumber + 1)
            .Select(offset => (int)parameters.StartDate.AddDays(offset).DayOfWeek)
            .Distinct()
            .ToList();

        var templatesQuery = _db.ProviderAvailabilityTemplates
            .AsNoTracking()
            .Where(t => t.IsActive && daysInRange.Contains(t.DayOfWeek));

        if (parameters.ProviderId.HasValue)
            templatesQuery = templatesQuery.Where(t => t.ProviderId == parameters.ProviderId.Value);

        if (!string.IsNullOrWhiteSpace(parameters.AppointmentType))
            templatesQuery = templatesQuery.Where(t => t.AppointmentType == parameters.AppointmentType);

        var templates = await templatesQuery
            .Select(t => new
            {
                t.ProviderId,
                t.ProviderName,
                t.DayOfWeek,
                t.StartTime,
                t.EndTime,
                t.SlotDurationMinutes,
                t.AppointmentType,
            })
            .ToListAsync(cancellationToken);

        // ── Query existing appointments in the date range ─────────────────────
        // Uses ix_appointments_appointment_time_status_provider_id composite index.
        var existingQuery = _db.Appointments
            .AsNoTracking()
            .Where(a => a.AppointmentTime >= startDt
                     && a.AppointmentTime <= endDt
                     && a.Status != AppointmentStatus.Cancelled);

        if (parameters.ProviderId.HasValue)
            existingQuery = existingQuery.Where(a => a.ProviderId == parameters.ProviderId.Value);

        var bookedSlots = (await existingQuery
            .Where(a => a.ProviderId.HasValue)
            .Select(a => new { ProviderId = a.ProviderId!.Value, a.AppointmentTime })
            .ToListAsync(cancellationToken))
            .Select(x => (x.ProviderId, x.AppointmentTime))
            .ToHashSet(BookedSlotComparer.Instance);

        // ── Build provider summary list ───────────────────────────────────────
        var providers = templates
            .GroupBy(t => t.ProviderId)
            .Select(g => new ProviderSummary(
                g.Key.ToString(),
                g.First().ProviderName))
            .ToList();

        // ── Generate all slots from templates across the date range ───────────
        var allSlots = new List<SlotItem>();

        for (var date = parameters.StartDate; date <= parameters.ResolvedEndDate; date = date.AddDays(1))
        {
            var dow = (int)date.DayOfWeek;

            foreach (var tpl in templates.Where(t => t.DayOfWeek == dow))
            {
                var slotStart = tpl.StartTime;
                while (slotStart.AddMinutes(tpl.SlotDurationMinutes) <= tpl.EndTime)
                {
                    var slotEnd  = slotStart.AddMinutes(tpl.SlotDurationMinutes);
                    var startUtc = date.ToDateTime(slotStart, DateTimeKind.Utc);
                    var available = !bookedSlots.Contains((tpl.ProviderId, startUtc));

                    allSlots.Add(new SlotItem(
                        SlotId:          BuildSlotId(date, slotStart, tpl.ProviderId),
                        Date:            date.ToString("yyyy-MM-dd"),
                        StartTime:       slotStart.ToString("HH:mm"),
                        EndTime:         slotEnd.ToString("HH:mm"),
                        ProviderName:    tpl.ProviderName,
                        ProviderId:      tpl.ProviderId.ToString(),
                        AppointmentType: tpl.AppointmentType,
                        Available:       available));

                    slotStart = slotStart.AddMinutes(tpl.SlotDurationMinutes);
                }
            }
        }

        // ── Build date summary for calendar dot indicators (AC-2) ─────────────
        var dateSummary = allSlots
            .GroupBy(s => s.Date)
            .Select(g => new DateAvailabilitySummary(
                Date:           g.Key,
                AvailableCount: g.Count(s => s.Available)))
            .ToList();

        var response = new SlotAvailabilityResponse(
            Slots:       allSlots.AsReadOnly(),
            Providers:   providers.AsReadOnly(),
            DateSummary: dateSummary.AsReadOnly());

        // ── Populate cache ────────────────────────────────────────────────────
        await _cache.SetAsync(cacheKey, response, CacheTtl, cancellationToken);

        _logger.LogInformation(
            "Slot query for {StartDate}–{EndDate} (provider={ProviderId}) returned {SlotCount} slots. " +
            "Cached with TTL {TtlMinutes}m.",
            parameters.StartDate, parameters.ResolvedEndDate,
            parameters.ProviderId?.ToString() ?? "all",
            allSlots.Count, CacheTtl.TotalMinutes);

        return response;
    }

    /// <inheritdoc/>
    public async Task InvalidateCacheAsync(
        DateOnly          date,
        Guid?             providerId        = null,
        CancellationToken cancellationToken = default)
    {
        // Invalidate the range key that would include this date.
        // Since keys are built from query params (which can vary), we invalidate the single-date
        // key that covers the affected day. Callers that queried a wider range will see a cache
        // miss on their next request and get fresh data.
        var singleDateKey = BuildSingleDateCacheKey(date, providerId);
        await _cache.RemoveAsync(singleDateKey, cancellationToken);

        _logger.LogDebug(
            "Slot cache invalidated for date {Date} (provider={ProviderId}), key={CacheKey}.",
            date, providerId?.ToString() ?? "all", singleDateKey);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static string BuildRangeCacheKey(SlotQueryParameters p)
    {
        var providerPart = p.ProviderId.HasValue ? p.ProviderId.Value.ToString() : "all";
        var typePart     = string.IsNullOrWhiteSpace(p.AppointmentType) ? "all" : p.AppointmentType;
        return $"slots:{p.StartDate:yyyy-MM-dd}:{p.ResolvedEndDate:yyyy-MM-dd}:{providerPart}:{typePart}";
    }

    private static string BuildSingleDateCacheKey(DateOnly date, Guid? providerId)
    {
        var providerPart = providerId.HasValue ? providerId.Value.ToString() : "all";
        return $"slots:{date:yyyy-MM-dd}:{date:yyyy-MM-dd}:{providerPart}:all";
    }

    /// <summary>
    /// Builds a stable, deterministic slot ID from (date, startTime, providerId).
    /// Deterministic so the same slot always returns the same ID across requests.
    /// Format: {date:yyyyMMdd}-{startTime:HHmm}-{providerGuid:N}
    /// </summary>
    private static string BuildSlotId(DateOnly date, TimeOnly startTime, Guid providerId)
        => $"{date:yyyyMMdd}-{startTime:HHmm}-{providerId:N}";
}

// ─────────────────────────────────────────────────────────────────────────────
// Internal helper — equality comparer for booked-slot HashSet lookup
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Value-equality comparer for anonymous booked-slot projection tuples.
/// Used internally by <see cref="AppointmentSlotService"/> to build an O(1)
/// lookup set of (ProviderId, AppointmentTime) pairs.
/// </summary>
file sealed class BookedSlotComparer
    : IEqualityComparer<(Guid ProviderId, DateTime AppointmentTime)>
{
    public static readonly BookedSlotComparer Instance = new();

    public bool Equals(
        (Guid ProviderId, DateTime AppointmentTime) x,
        (Guid ProviderId, DateTime AppointmentTime) y)
        => x.ProviderId == y.ProviderId && x.AppointmentTime == y.AppointmentTime;

    public int GetHashCode((Guid ProviderId, DateTime AppointmentTime) obj)
        => HashCode.Combine(obj.ProviderId, obj.AppointmentTime);
}
