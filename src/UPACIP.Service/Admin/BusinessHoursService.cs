using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Auth;
using UPACIP.Service.Caching;

namespace UPACIP.Service.Admin;

/// <summary>
/// Manages clinic-wide business hours and holiday definitions (US_059 AC-3, AC-4).
///
/// Redis cache keys (5-min TTL, NFR-030):
///   <c>config:hours</c>    — all 7 days of operating hours
///   <c>config:holidays</c> — active (non-deleted) holiday list
///
/// Every write appends an AuditLog entry (NFR-012, NFR-035).
/// </summary>
public sealed class BusinessHoursService : IBusinessHoursService
{
    private const string CacheHours    = "config:hours";
    private const string CacheHolidays = "config:holidays";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly ApplicationDbContext         _db;
    private readonly ICacheService                _cache;
    private readonly IAuditLogService             _audit;
    private readonly ILogger<BusinessHoursService> _logger;

    public BusinessHoursService(
        ApplicationDbContext          db,
        ICacheService                 cache,
        IAuditLogService              audit,
        ILogger<BusinessHoursService> logger)
    {
        _db     = db;
        _cache  = cache;
        _audit  = audit;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Business Hours
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BusinessHoursEntryDto>> GetAllAsync(
        CancellationToken ct = default)
    {
        var cached = await _cache.GetAsync<List<BusinessHoursEntryDto>>(CacheHours, ct);
        if (cached is not null) return cached;

        var rows = await _db.BusinessHours
            .AsNoTracking()
            .OrderBy(h => h.DayOfWeek)
            .Select(h => new BusinessHoursEntryDto(
                h.BusinessHoursId,
                h.DayOfWeek,
                h.OpenTime,
                h.CloseTime,
                h.IsClosed))
            .ToListAsync(ct);

        await _cache.SetAsync(CacheHours, rows, CacheTtl, ct);
        return rows;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<BusinessHoursEntryDto>> UpdateAllAsync(
        UpdateBusinessHoursRequest request,
        Guid                       adminUserId,
        CancellationToken          ct = default)
    {
        // Load all tracked rows once; update only the ones present in the request.
        var dbRows = await _db.BusinessHours.ToDictionaryAsync(h => h.BusinessHoursId, ct);

        foreach (var entry in request.Entries)
        {
            if (!dbRows.TryGetValue(entry.BusinessHoursId, out var row))
                continue; // ignore entries with unknown IDs — don't create phantom rows

            row.OpenTime        = entry.IsClosed ? null : entry.OpenTime;
            row.CloseTime       = entry.IsClosed ? null : entry.CloseTime;
            row.IsClosed        = entry.IsClosed;
            row.UpdatedAt       = DateTime.UtcNow;
            row.UpdatedByUserId = adminUserId;
        }

        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            AuditAction.DataModify,
            adminUserId,
            resourceType:      "BusinessHours",
            ipAddress:         string.Empty,
            userAgent:         string.Empty,
            cancellationToken: ct);

        await _cache.RemoveAsync(CacheHours, ct);

        return await GetAllAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Holidays
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyList<HolidayResponse>> GetHolidaysAsync(
        CancellationToken ct = default)
    {
        var cached = await _cache.GetAsync<List<HolidayResponse>>(CacheHolidays, ct);
        if (cached is not null) return cached;

        var rows = await _db.Holidays
            .AsNoTracking()
            .Where(h => h.DeletedAt == null)
            .OrderBy(h => h.Date)
            .Select(h => new HolidayResponse(
                h.HolidayId,
                h.Date,
                h.Name,
                h.IsRecurring,
                h.IsHalfDay,
                h.CreatedAt))
            .ToListAsync(ct);

        await _cache.SetAsync(CacheHolidays, rows, CacheTtl, ct);
        return rows;
    }

    /// <inheritdoc/>
    public async Task<AddHolidayResponse> AddHolidayAsync(
        CreateHolidayRequest request,
        Guid                 adminUserId,
        CancellationToken    ct = default)
    {
        var holiday = new Holiday
        {
            HolidayId       = Guid.NewGuid(),
            Date            = request.Date,
            Name            = request.Name,
            IsRecurring     = request.IsRecurring,
            IsHalfDay       = request.IsHalfDay,
            CreatedByUserId = adminUserId,
            CreatedAt       = DateTime.UtcNow,
            DeletedAt       = null,
        };

        _db.Holidays.Add(holiday);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Holiday '{Name}' on {Date} created by admin {AdminId}.",
            holiday.Name, holiday.Date, adminUserId);

        await _audit.LogAsync(
            AuditAction.DataModify,
            adminUserId,
            resourceType:      "Holiday",
            ipAddress:         string.Empty,
            userAgent:         string.Empty,
            resourceId:        holiday.HolidayId,
            cancellationToken: ct);

        await _cache.RemoveAsync(CacheHolidays, ct);

        // ── Query existing appointments on this date for the admin review panel ──
        // Appointments are stored as UTC DateTime; match on the UTC date component.
        var holidayDateUtc  = holiday.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var holidayDateNext = holidayDateUtc.AddDays(1);

        var affected = await _db.Appointments
            .AsNoTracking()
            .Where(a =>
                a.AppointmentTime >= holidayDateUtc
             && a.AppointmentTime  < holidayDateNext
             && a.Status           != AppointmentStatus.Cancelled)
            .OrderBy(a => a.AppointmentTime)
            .Select(a => new AffectedAppointmentDto(
                a.Id,
                a.BookingReference,
                a.AppointmentTime,
                a.ProviderName,
                a.AppointmentType))
            .ToListAsync(ct);

        if (affected.Count > 0)
        {
            _logger.LogWarning(
                "Holiday '{Name}' on {Date} affects {Count} existing appointments — " +
                "admin review required.",
                holiday.Name, holiday.Date, affected.Count);
        }

        var holidayResponse = new HolidayResponse(
            holiday.HolidayId,
            holiday.Date,
            holiday.Name,
            holiday.IsRecurring,
            holiday.IsHalfDay,
            holiday.CreatedAt);

        return new AddHolidayResponse(holidayResponse, affected.Count, affected);
    }

    /// <inheritdoc/>
    public async Task<bool> RemoveHolidayAsync(
        Guid              holidayId,
        Guid              adminUserId,
        CancellationToken ct = default)
    {
        var holiday = await _db.Holidays
            .FirstOrDefaultAsync(h => h.HolidayId == holidayId && h.DeletedAt == null, ct);

        if (holiday is null)
        {
            _logger.LogWarning(
                "RemoveHoliday: holiday {HolidayId} not found or already deleted.", holidayId);
            return false;
        }

        holiday.DeletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await _audit.LogAsync(
            AuditAction.DataDelete,
            adminUserId,
            resourceType:      "Holiday",
            ipAddress:         string.Empty,
            userAgent:         string.Empty,
            resourceId:        holidayId,
            cancellationToken: ct);

        await _cache.RemoveAsync(CacheHolidays, ct);
        return true;
    }
}
