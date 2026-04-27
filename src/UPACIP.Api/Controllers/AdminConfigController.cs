using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using FluentValidation;
using UPACIP.Api.Authorization;
using UPACIP.Service.Admin;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Admin-only CRUD endpoints for platform configuration (US_058 AC-4).
///
/// Routes:
///   GET  /api/admin/config/slots              — Retrieve weekly slot templates per provider.
///   PUT  /api/admin/config/slots              — Update provider slot templates.
///   GET  /api/admin/config/notifications      — Retrieve notification templates.
///   PUT  /api/admin/config/notifications      — Update / upsert a notification template.
///   POST /api/admin/config/notifications      — Add a new notification template.
///   GET  /api/admin/config/hours              — Retrieve business hours and holidays.
///   PUT  /api/admin/config/hours              — Update business hours and holidays.
///   GET  /api/admin/config/risk-thresholds    — Retrieve AI risk threshold settings.
///   PUT  /api/admin/config/risk-thresholds    — Update AI risk threshold settings.
///
/// Authorization (OWASP A01, NFR-011):
///   All endpoints require the Admin role.
///
/// Validation (NFR-018):
///   Data annotations on DTOs enforce field-level constraints.
///   Business-rule violations return 422 Unprocessable Entity.
///
/// Caching (NFR-030):
///   Configuration service caches reads in Redis (5-min TTL) and
///   cache-busts on each write.
///
/// Audit logging (NFR-012, NFR-035):
///   Every write appends an AuditLog entry with admin user ID and correlation ID.
/// </summary>
[ApiController]
[Authorize(Policy = RbacPolicies.AdminOnly)]
[Produces("application/json")]
public sealed class AdminConfigController : ControllerBase
{
    private readonly IConfigurationService             _config;
    private readonly INotificationTemplateService      _notifTemplates;
    private readonly IRiskConfigService                _riskConfig;
    private readonly IValidator<UpdateNotificationTemplateByIdRequest> _templateValidator;
    private readonly IValidator<UpdateRiskConfigRequest>               _riskValidator;
    private readonly ILogger<AdminConfigController>    _logger;

    public AdminConfigController(
        IConfigurationService          config,
        INotificationTemplateService   notifTemplates,
        IRiskConfigService             riskConfig,
        IValidator<UpdateNotificationTemplateByIdRequest> templateValidator,
        IValidator<UpdateRiskConfigRequest>               riskValidator,
        ILogger<AdminConfigController> logger)
    {
        _config            = config;
        _notifTemplates    = notifTemplates;
        _riskConfig        = riskConfig;
        _templateValidator = templateValidator;
        _riskValidator     = riskValidator;
        _logger            = logger;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Guid AdminUserId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/admin/config/slots
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("api/admin/config/slots")]
    [ProducesResponseType(typeof(SlotTemplatesConfigDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSlotTemplatesAsync(CancellationToken ct)
    {
        _logger.LogDebug("AdminConfig GET slots — admin={AdminId}", AdminUserId);
        return Ok(await _config.GetSlotTemplatesAsync(ct));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/admin/config/slots
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPut("api/admin/config/slots")]
    [ProducesResponseType(typeof(SlotTemplatesConfigDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateSlotTemplatesAsync(
        [FromBody] SlotTemplatesConfigDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return UnprocessableEntity(new ValidationProblemDetails(ModelState));

        _logger.LogInformation(
            "AdminConfig PUT slots by {AdminId} — {Count} providers, CorrelationId={CorrelationId}",
            AdminUserId, dto.Providers.Count, HttpContext.TraceIdentifier);

        var result = await _config.UpdateSlotTemplatesAsync(dto, AdminUserId, ct);
        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/admin/config/notifications
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("api/admin/config/notifications")]
    [ProducesResponseType(typeof(NotificationTemplatesConfigDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetNotificationTemplatesAsync(CancellationToken ct)
    {
        return Ok(await _config.GetNotificationTemplatesAsync(ct));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/admin/config/notifications — update existing template
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPut("api/admin/config/notifications")]
    [ProducesResponseType(typeof(NotificationTemplatesConfigDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateNotificationTemplateAsync(
        [FromBody] NotificationTemplateDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return UnprocessableEntity(new ValidationProblemDetails(ModelState));

        _logger.LogInformation(
            "AdminConfig PUT notification template '{TemplateId}' by {AdminId}, CorrelationId={CorrelationId}",
            dto.Id, AdminUserId, HttpContext.TraceIdentifier);

        return Ok(await _config.UpdateNotificationTemplateAsync(dto, AdminUserId, ct));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/admin/config/notifications — add new template
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPost("api/admin/config/notifications")]
    [ProducesResponseType(typeof(NotificationTemplateDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AddNotificationTemplateAsync(
        [FromBody] NotificationTemplateDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return UnprocessableEntity(new ValidationProblemDetails(ModelState));

        _logger.LogInformation(
            "AdminConfig POST notification template '{Name}' by {AdminId}, CorrelationId={CorrelationId}",
            dto.Name, AdminUserId, HttpContext.TraceIdentifier);

        var created = await _config.AddNotificationTemplateAsync(dto, AdminUserId, ct);
        return Created($"/api/admin/config/notifications/{created.Id}", created);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/admin/config/hours
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("api/admin/config/hours")]
    [ProducesResponseType(typeof(BusinessHoursConfigDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetBusinessHoursAsync(CancellationToken ct)
    {
        return Ok(await _config.GetBusinessHoursAsync(ct));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/admin/config/hours
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPut("api/admin/config/hours")]
    [ProducesResponseType(typeof(BusinessHoursConfigDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateBusinessHoursAsync(
        [FromBody] BusinessHoursConfigDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return UnprocessableEntity(new ValidationProblemDetails(ModelState));

        _logger.LogInformation(
            "AdminConfig PUT hours by {AdminId}, CorrelationId={CorrelationId}",
            AdminUserId, HttpContext.TraceIdentifier);

        return Ok(await _config.UpdateBusinessHoursAsync(dto, AdminUserId, ct));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/admin/config/risk-thresholds
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("api/admin/config/risk-thresholds")]
    [ProducesResponseType(typeof(RiskThresholdsConfigDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetRiskThresholdsAsync(CancellationToken ct)
    {
        return Ok(await _config.GetRiskThresholdsAsync(ct));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/admin/config/risk-thresholds
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPut("api/admin/config/risk-thresholds")]
    [ProducesResponseType(typeof(RiskThresholdsConfigDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateRiskThresholdsAsync(
        [FromBody] RiskThresholdsConfigDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return UnprocessableEntity(new ValidationProblemDetails(ModelState));

        // Business-rule: medium must be less than high.
        if (dto.MediumRiskThreshold >= dto.HighRiskThreshold)
        {
            ModelState.AddModelError(
                nameof(dto.MediumRiskThreshold),
                "MediumRiskThreshold must be less than HighRiskThreshold.");
            return UnprocessableEntity(new ValidationProblemDetails(ModelState));
        }

        _logger.LogInformation(
            "AdminConfig PUT risk-thresholds by {AdminId}, CorrelationId={CorrelationId}",
            AdminUserId, HttpContext.TraceIdentifier);

        return Ok(await _config.UpdateRiskThresholdsAsync(dto, AdminUserId, ct));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // US_060 — GET /api/admin/config/notifications/{id}
    // Returns a single template by ID (AC-1).
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("api/admin/config/notifications/{id}")]
    [ProducesResponseType(typeof(NotificationTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetNotificationTemplateByIdAsync(
        string id, CancellationToken ct)
    {
        _logger.LogDebug(
            "AdminConfig GET notification/{TemplateId} — admin={AdminId}", id, AdminUserId);

        var template = await _notifTemplates.GetByIdAsync(id, ct);
        return template is null ? NotFound() : Ok(template);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // US_060 — PUT /api/admin/config/notifications/{id}
    // Updates a single template with variable placeholder validation (AC-1, AC-2).
    // Returns 422 with specific error for unrecognised {{tokens}}.
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPut("api/admin/config/notifications/{id}")]
    [ProducesResponseType(typeof(NotificationTemplateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateNotificationTemplateByIdAsync(
        string id,
        [FromBody] UpdateNotificationTemplateByIdRequest request,
        CancellationToken ct)
    {
        // FluentValidation — validates placeholder syntax, channel/status values.
        var validation = await _templateValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
                ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            return UnprocessableEntity(new ValidationProblemDetails(ModelState));
        }

        _logger.LogInformation(
            "AdminConfig PUT notification/{TemplateId} by {AdminId}, CorrelationId={CorrelationId}",
            id, AdminUserId, HttpContext.TraceIdentifier);

        try
        {
            var updated = await _notifTemplates.UpdateByIdAsync(id, request, AdminUserId, ct);
            return Ok(updated);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // US_060 — GET /api/admin/config/risk
    // Returns full risk configuration including scoring parameter weights (AC-3).
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("api/admin/config/risk")]
    [ProducesResponseType(typeof(RiskConfigDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetRiskConfigAsync(CancellationToken ct)
    {
        _logger.LogDebug("AdminConfig GET risk — admin={AdminId}", AdminUserId);
        return Ok(await _riskConfig.GetAsync(ct));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // US_060 — PUT /api/admin/config/risk
    // Updates risk thresholds + scoring params.  Sets RecalculationPending flag
    // (deferred batch recalculation for existing appointments, edge case).
    // ─────────────────────────────────────────────────────────────────────────

    [HttpPut("api/admin/config/risk")]
    [ProducesResponseType(typeof(RiskConfigDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateRiskConfigAsync(
        [FromBody] UpdateRiskConfigRequest request,
        CancellationToken ct)
    {
        // FluentValidation — validates thresholds, weight sum-to-1.0.
        var validation = await _riskValidator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
                ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            return UnprocessableEntity(new ValidationProblemDetails(ModelState));
        }

        _logger.LogInformation(
            "AdminConfig PUT risk by {AdminId}, CorrelationId={CorrelationId}",
            AdminUserId, HttpContext.TraceIdentifier);

        var updated = await _riskConfig.UpdateAsync(request, AdminUserId, ct);
        return Ok(updated);
    }
}
