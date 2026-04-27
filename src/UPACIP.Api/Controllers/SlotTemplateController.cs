using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using UPACIP.Api.Authorization;
using UPACIP.Service.Admin;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Admin-only endpoints for managing provider appointment slot templates (US_059 AC-1, AC-2).
///
/// Routes:
///   GET  /api/admin/config/slots/{providerId}                               — All templates for a provider.
///   GET  /api/admin/config/slots/{providerId}/{dayOfWeek}                   — Single template (provider + day).
///   GET  /api/admin/config/slots/{providerId}/{dayOfWeek}/affected          — Appointments affected by a template change.
///   PUT  /api/admin/config/slots/{providerId}/{dayOfWeek}                   — Create or replace template.
///
/// Authorization: Admin only (OWASP A01, NFR-011).
/// Caching: Reads served from Redis cache (5-min TTL); cache invalidated on every write.
/// Audit: Every write appends an AuditLog entry with admin attribution (NFR-012).
/// Concurrency: PUT returns 409 Conflict when client Version is stale (DR-015).
/// </summary>
[ApiController]
[Authorize(Policy = RbacPolicies.AdminOnly)]
[Produces("application/json")]
public sealed class SlotTemplateController : ControllerBase
{
    private readonly ISlotTemplateService             _service;
    private readonly ILogger<SlotTemplateController>  _logger;

    public SlotTemplateController(
        ISlotTemplateService            service,
        ILogger<SlotTemplateController> logger)
    {
        _service = service;
        _logger  = logger;
    }

    private Guid AdminUserId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    // ── GET /api/admin/config/slots/{providerId} ──────────────────────────────

    /// <summary>Returns all configured slot templates for the given provider (all days).</summary>
    [HttpGet("api/admin/config/slots/{providerId:guid}")]
    [ProducesResponseType(typeof(IReadOnlyList<SlotTemplateResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetByProviderAsync(
        Guid              providerId,
        CancellationToken ct)
    {
        _logger.LogDebug("SlotTemplate GET provider={ProviderId} — admin={AdminId}",
            providerId, AdminUserId);

        var templates = await _service.GetByProviderAsync(providerId, ct);
        return Ok(templates);
    }

    // ── GET /api/admin/config/slots/{providerId}/{dayOfWeek} ──────────────────

    /// <summary>Returns the slot template for a specific provider/day combination, or 404 if not configured.</summary>
    [HttpGet("api/admin/config/slots/{providerId:guid}/{dayOfWeek:int}")]
    [ProducesResponseType(typeof(SlotTemplateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetByProviderAndDayAsync(
        Guid              providerId,
        int               dayOfWeek,
        CancellationToken ct)
    {
        if (dayOfWeek < 0 || dayOfWeek > 6)
            return BadRequest(new { error = "dayOfWeek must be between 0 (Sunday) and 6 (Saturday)." });

        _logger.LogDebug("SlotTemplate GET provider={ProviderId} day={DayOfWeek} — admin={AdminId}",
            providerId, dayOfWeek, AdminUserId);

        var template = await _service.GetByProviderAndDayAsync(providerId, dayOfWeek, ct);
        return template is null ? NotFound() : Ok(template);
    }

    // ── GET /api/admin/config/slots/{providerId}/{dayOfWeek}/affected ─────────

    /// <summary>
    /// Returns the non-cancelled appointments that fall on the given provider's template day,
    /// enabling a conflict-preview before the admin commits a template change.
    /// </summary>
    [HttpGet("api/admin/config/slots/{providerId:guid}/{dayOfWeek:int}/affected")]
    [ProducesResponseType(typeof(AffectedAppointmentsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAffectedAppointmentsAsync(
        Guid              providerId,
        int               dayOfWeek,
        CancellationToken ct)
    {
        if (dayOfWeek < 0 || dayOfWeek > 6)
            return BadRequest(new { error = "dayOfWeek must be between 0 (Sunday) and 6 (Saturday)." });

        _logger.LogDebug(
            "SlotTemplate affected-appointments provider={ProviderId} day={DayOfWeek} — admin={AdminId}",
            providerId, dayOfWeek, AdminUserId);

        var result = await _service.GetAffectedAppointmentsAsync(providerId, dayOfWeek, ct);
        return Ok(result);
    }

    // ── PUT /api/admin/config/slots/{providerId}/{dayOfWeek} ──────────────────

    /// <summary>
    /// Creates or replaces the slot template for the given provider/day combination.
    /// Returns 409 Conflict when the client Version is stale (optimistic concurrency).
    /// </summary>
    [HttpPut("api/admin/config/slots/{providerId:guid}/{dayOfWeek:int}")]
    [ProducesResponseType(typeof(SlotTemplateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpsertAsync(
        Guid                      providerId,
        int                       dayOfWeek,
        [FromBody] UpsertSlotTemplateRequest body,
        CancellationToken         ct)
    {
        if (dayOfWeek < 0 || dayOfWeek > 6)
            return BadRequest(new { error = "dayOfWeek must be between 0 (Sunday) and 6 (Saturday)." });

        if (!ModelState.IsValid)
            return UnprocessableEntity(new ValidationProblemDetails(ModelState));

        _logger.LogInformation(
            "SlotTemplate PUT provider={ProviderId} day={DayOfWeek} blocks={BlockCount} " +
            "version={Version} — admin={AdminId}, CorrelationId={CorrelationId}",
            providerId, dayOfWeek, body.Blocks?.Count ?? 0, body.Version,
            AdminUserId, HttpContext.TraceIdentifier);

        var result = await _service.UpsertAsync(providerId, dayOfWeek, body, AdminUserId, ct);

        return result.Status switch
        {
            SlotTemplateUpsertStatus.Success =>
                Ok(result.Template),

            SlotTemplateUpsertStatus.ConcurrencyConflict =>
                Conflict(new { error = "Template was modified by another request. Re-fetch and retry." }),

            SlotTemplateUpsertStatus.ProviderNotFound =>
                NotFound(new { error = $"Provider '{providerId}' not found." }),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }
}
