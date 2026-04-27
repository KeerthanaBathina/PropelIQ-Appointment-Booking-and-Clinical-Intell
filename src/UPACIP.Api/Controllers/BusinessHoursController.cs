using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using UPACIP.Api.Authorization;
using UPACIP.Service.Admin;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Admin-only endpoints for managing clinic business hours and holidays (US_059 AC-3, AC-4).
///
/// Routes:
///   GET  /api/admin/config/business-hours         — All 7 days of operating hours.
///   PUT  /api/admin/config/business-hours         — Bulk update operating hours.
///   GET  /api/admin/config/holidays               — All active (non-deleted) holidays.
///   POST /api/admin/config/holidays               — Add a new holiday; returns affected appointments.
///   DELETE /api/admin/config/holidays/{id}        — Soft-delete a holiday.
///
/// Note on route naming: /api/admin/config/business-hours is used instead of
/// /api/admin/config/hours to avoid a routing conflict with the existing AdminConfigController
/// endpoint that manages the legacy JSON-blob hours configuration for US_058.
///
/// Authorization: Admin only (OWASP A01, NFR-011).
/// Caching: Reads served from Redis cache (5-min TTL); cache invalidated on every write.
/// Audit: Every write appends an AuditLog entry with admin attribution (NFR-012).
/// </summary>
[ApiController]
[Authorize(Policy = RbacPolicies.AdminOnly)]
[Produces("application/json")]
public sealed class BusinessHoursController : ControllerBase
{
    private readonly IBusinessHoursService             _service;
    private readonly ILogger<BusinessHoursController>  _logger;

    public BusinessHoursController(
        IBusinessHoursService            service,
        ILogger<BusinessHoursController> logger)
    {
        _service = service;
        _logger  = logger;
    }

    private Guid AdminUserId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    // ── GET /api/admin/config/business-hours ──────────────────────────────────

    /// <summary>Returns the operating hours for all 7 days of the week.</summary>
    [HttpGet("api/admin/config/business-hours")]
    [ProducesResponseType(typeof(IReadOnlyList<BusinessHoursEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAllAsync(CancellationToken ct)
    {
        _logger.LogDebug("BusinessHours GET — admin={AdminId}", AdminUserId);
        return Ok(await _service.GetAllAsync(ct));
    }

    // ── PUT /api/admin/config/business-hours ──────────────────────────────────

    /// <summary>
    /// Bulk-updates operating hours.  Partial updates are supported — days not included
    /// in the request are left unchanged.  Returns the full 7-day schedule after the update.
    /// </summary>
    [HttpPut("api/admin/config/business-hours")]
    [ProducesResponseType(typeof(IReadOnlyList<BusinessHoursEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateAllAsync(
        [FromBody] UpdateBusinessHoursRequest body,
        CancellationToken                    ct)
    {
        if (!ModelState.IsValid)
            return UnprocessableEntity(new ValidationProblemDetails(ModelState));

        _logger.LogInformation(
            "BusinessHours PUT {Count} entries — admin={AdminId}, CorrelationId={CorrelationId}",
            body.Entries?.Count ?? 0, AdminUserId, HttpContext.TraceIdentifier);

        var result = await _service.UpdateAllAsync(body, AdminUserId, ct);
        return Ok(result);
    }

    // ── GET /api/admin/config/holidays ────────────────────────────────────────

    /// <summary>Returns all active (non-deleted) holiday definitions ordered by date.</summary>
    [HttpGet("api/admin/config/holidays")]
    [ProducesResponseType(typeof(IReadOnlyList<HolidayResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetHolidaysAsync(CancellationToken ct)
    {
        _logger.LogDebug("Holidays GET — admin={AdminId}", AdminUserId);
        return Ok(await _service.GetHolidaysAsync(ct));
    }

    // ── POST /api/admin/config/holidays ───────────────────────────────────────

    /// <summary>
    /// Creates a new holiday definition and returns it together with a list of existing
    /// non-cancelled appointments on the same date for admin review (AC-4).
    /// </summary>
    [HttpPost("api/admin/config/holidays")]
    [ProducesResponseType(typeof(AddHolidayResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AddHolidayAsync(
        [FromBody] CreateHolidayRequest body,
        CancellationToken               ct)
    {
        if (!ModelState.IsValid)
            return UnprocessableEntity(new ValidationProblemDetails(ModelState));

        _logger.LogInformation(
            "Holiday POST '{Name}' on {Date} recurring={IsRecurring} — admin={AdminId}, CorrelationId={CorrelationId}",
            body.Name, body.Date, body.IsRecurring, AdminUserId, HttpContext.TraceIdentifier);

        var result = await _service.AddHolidayAsync(body, AdminUserId, ct);

        return Created(
            $"/api/admin/config/holidays/{result.Holiday.HolidayId}",
            result);
    }

    // ── DELETE /api/admin/config/holidays/{id} ────────────────────────────────

    /// <summary>Soft-deletes the holiday with the given id. Returns 404 if not found or already deleted.</summary>
    [HttpDelete("api/admin/config/holidays/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RemoveHolidayAsync(
        Guid              id,
        CancellationToken ct)
    {
        _logger.LogInformation(
            "Holiday DELETE {HolidayId} — admin={AdminId}, CorrelationId={CorrelationId}",
            id, AdminUserId, HttpContext.TraceIdentifier);

        var removed = await _service.RemoveHolidayAsync(id, AdminUserId, ct);
        return removed ? NoContent() : NotFound();
    }
}
