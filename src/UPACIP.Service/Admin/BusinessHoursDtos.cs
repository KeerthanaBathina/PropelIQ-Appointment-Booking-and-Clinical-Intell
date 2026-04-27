using System.ComponentModel.DataAnnotations;

namespace UPACIP.Service.Admin;

// ─── Business Hours ───────────────────────────────────────────────────────────

/// <summary>
/// One day-of-week entry in the clinic's operating hours schedule.
/// Used in both GET responses and PUT request bodies.
/// </summary>
public sealed record BusinessHoursEntryDto(
    Guid          BusinessHoursId,
    /// <summary>0 = Sunday … 6 = Saturday (matches <see cref="System.DayOfWeek"/>).</summary>
    [Range(0, 6)] int     DayOfWeek,
    /// <summary>Opening time. Null when <see cref="IsClosed"/> is <c>true</c>.</summary>
    TimeOnly?     OpenTime,
    /// <summary>Closing time. Null when <see cref="IsClosed"/> is <c>true</c>.</summary>
    TimeOnly?     CloseTime,
    /// <summary>When <c>true</c> the clinic is closed on this day.</summary>
    bool          IsClosed);

/// <summary>
/// Request body for <c>PUT api/admin/config/business-hours</c>.
/// Must supply exactly 7 entries — one per day of the week (0–6).
/// </summary>
public sealed record UpdateBusinessHoursRequest(
    [Required] IReadOnlyList<BusinessHoursEntryDto> Entries);

// ─── Holidays ────────────────────────────────────────────────────────────────

/// <summary>Response for a single holiday record.</summary>
public sealed record HolidayResponse(
    Guid          HolidayId,
    DateOnly      Date,
    [MaxLength(200)] string Name,
    bool          IsRecurring,
    bool          IsHalfDay,
    DateTime      CreatedAt);

/// <summary>
/// Request body for <c>POST api/admin/config/holidays</c>.
/// </summary>
public sealed record CreateHolidayRequest(
    /// <summary>Calendar date to block. Must not be in the past for non-recurring holidays.</summary>
    [Required] DateOnly Date,
    /// <summary>Human-readable name shown in the Admin UI and staff calendar (max 200 chars).</summary>
    [Required, MaxLength(200)] string Name,
    /// <summary>
    /// When <c>true</c>, the holiday recurs annually on the same calendar day (month + day).
    /// Past dates are permitted for recurring holidays (e.g. seeding Christmas = Dec 25).
    /// </summary>
    bool IsRecurring,
    /// <summary>When <c>true</c> only the afternoon is blocked; morning slots remain available.</summary>
    bool IsHalfDay);

/// <summary>
/// Response for <c>POST api/admin/config/holidays</c>.
/// Returns the newly created holiday together with a list of existing non-cancelled
/// appointments on the same date so the admin can take follow-up action.
/// </summary>
public sealed record AddHolidayResponse(
    HolidayResponse                       Holiday,
    int                                   AffectedAppointmentCount,
    IReadOnlyList<AffectedAppointmentDto> AffectedAppointments);
