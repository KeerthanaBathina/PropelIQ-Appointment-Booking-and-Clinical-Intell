using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using UPACIP.Api.Authorization;
using UPACIP.Service.Admin;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Admin-only user management endpoints (US_058 AC-3, US_061 AC-1 – AC-4).
///
/// Routes:
///   GET  /api/admin/users                   — List all staff and admin accounts (AC-2).
///   POST /api/admin/users                   — Create staff account with role + temp password (AC-1).
///   POST /api/admin/users/invite            — Legacy invite alias (US_058 backward-compat).
///   PUT  /api/admin/users/{id}/status       — Activate or deactivate via status body (US_058).
///   PUT  /api/admin/users/{id}/deactivate   — Soft-deactivate with EC-1/EC-2 guards (AC-3).
///   PUT  /api/admin/users/{id}/reactivate   — Restore deactivated account (AC-4).
///
/// Authorization (OWASP A01, NFR-011):
///   All endpoints require the Admin role.
///   Users cannot deactivate themselves (EC-1 guard in AdminUserService).
///   Last-admin deactivation is blocked (EC-2 guard in AdminUserService).
///
/// Audit logging (NFR-012, NFR-035):
///   Every write appends an AuditLog entry with admin user ID and
///   <see cref="HttpContext.TraceIdentifier"/> correlation ID.
/// </summary>
[ApiController]
[Authorize(Policy = RbacPolicies.AdminOnly)]
[Produces("application/json")]
public sealed class AdminUserController : ControllerBase
{
    private readonly IAdminUserService             _userService;
    private readonly ILogger<AdminUserController>  _logger;

    public AdminUserController(
        IAdminUserService            userService,
        ILogger<AdminUserController> logger)
    {
        _userService = userService;
        _logger      = logger;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Guid AdminUserId =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/admin/users
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all staff and admin accounts with status, last login, and role (AC-3).
    /// </summary>
    [HttpGet("api/admin/users")]
    [ProducesResponseType(typeof(AdminUsersResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUsersAsync(CancellationToken ct)
    {
        _logger.LogDebug(
            "AdminUsers GET — admin={AdminId}, CorrelationId={CorrelationId}",
            AdminUserId, HttpContext.TraceIdentifier);

        return Ok(await _userService.GetAllUsersAsync(ct));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/admin/users/invite
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Invites a new staff or admin user. Creates the account in Active state.
    /// </summary>
    [HttpPost("api/admin/users/invite")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> InviteUserAsync(
        [FromBody] InviteUserRequestDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return UnprocessableEntity(new ValidationProblemDetails(ModelState));

        _logger.LogInformation(
            "AdminUsers POST invite {Email} role={Role} by admin {AdminId}, CorrelationId={CorrelationId}",
            dto.Email, dto.Role, AdminUserId, HttpContext.TraceIdentifier);

        try
        {
            var created = await _userService.InviteUserAsync(dto, AdminUserId, ct);
            return Created($"/api/admin/users/{created.Id}", created);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            // Return 409 Conflict without leaking existence details (OWASP A07).
            return Conflict(new ProblemDetails
            {
                Title  = "Conflict",
                Detail = "An account with this email is already registered.",
                Status = StatusCodes.Status409Conflict,
            });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError("role", ex.Message);
            return UnprocessableEntity(new ValidationProblemDetails(ModelState));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/admin/users/{id}/status
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets a user's account status to Active or Inactive (soft-deactivation, FR-088).
    /// No data is deleted — historical records are fully preserved.
    /// </summary>
    [HttpPut("api/admin/users/{id}/status")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SetUserStatusAsync(
        [FromRoute] string id,
        [FromBody]  SetUserStatusRequestDto dto,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return UnprocessableEntity(new ValidationProblemDetails(ModelState));

        var allowed = new[] { "Active", "Inactive" };
        if (!allowed.Contains(dto.Status, StringComparer.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(dto.Status), "Status must be 'Active' or 'Inactive'.");
            return UnprocessableEntity(new ValidationProblemDetails(ModelState));
        }

        _logger.LogInformation(
            "AdminUsers PUT status={Status} for user {UserId} by admin {AdminId}, CorrelationId={CorrelationId}",
            dto.Status, id, AdminUserId, HttpContext.TraceIdentifier);

        try
        {
            var updated = await _userService.SetUserStatusAsync(id, dto.Status, AdminUserId, ct);
            return Ok(updated);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ProblemDetails
            {
                Title  = "Not Found",
                Detail = $"User '{id}' was not found.",
                Status = StatusCodes.Status404NotFound,
            });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title  = "Unprocessable Entity",
                Detail = ex.Message,
                Status = StatusCodes.Status422UnprocessableEntity,
            });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/admin/users   (US_061 AC-1)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new staff or admin account.  Provisions the account with role assignment,
    /// a cryptographically secure temporary password, and an email invitation (AC-1).
    /// Delegates to <see cref="IAdminUserService.InviteUserAsync"/>.
    /// </summary>
    [HttpPost("api/admin/users")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateStaffAsync(
        [FromBody] InviteUserRequestDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return UnprocessableEntity(new ValidationProblemDetails(ModelState));

        _logger.LogInformation(
            "AdminUsers POST create {Email} role={Role} by admin {AdminId}, CorrelationId={CorrelationId}",
            dto.Email, dto.Role, AdminUserId, HttpContext.TraceIdentifier);

        try
        {
            var created = await _userService.InviteUserAsync(dto, AdminUserId, ct);
            return Created($"/api/admin/users/{created.Id}", created);
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase))
        {
            return Conflict(new ProblemDetails
            {
                Title  = "Conflict",
                Detail = "An account with this email is already registered.",
                Status = StatusCodes.Status409Conflict,
            });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError("role", ex.Message);
            return UnprocessableEntity(new ValidationProblemDetails(ModelState));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/admin/users/{id}/deactivate   (US_061 AC-3)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Soft-deactivates a staff account.  The account is disabled (cannot log in) but all
    /// historical data — audit logs, actions, verifications — is preserved (FR-088, AC-3).
    ///
    /// Business rule guards:
    ///   EC-1 — Returns 422 "Cannot deactivate your own account."
    ///   EC-2 — Returns 409 "At least one active admin account required."
    /// </summary>
    [HttpPut("api/admin/users/{id}/deactivate")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeactivateStaffAsync(
        [FromRoute] string id, CancellationToken ct)
    {
        _logger.LogInformation(
            "AdminUsers PUT deactivate user={UserId} by admin={AdminId}, CorrelationId={CorrelationId}",
            id, AdminUserId, HttpContext.TraceIdentifier);

        try
        {
            var updated = await _userService.DeactivateStaffAsync(id, AdminUserId, ct);
            return Ok(updated);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ProblemDetails
            {
                Title  = "Not Found",
                Detail = $"User '{id}' was not found.",
                Status = StatusCodes.Status404NotFound,
            });
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("At least one active admin", StringComparison.OrdinalIgnoreCase))
        {
            // EC-2: last-admin guard — return 409 Conflict
            return Conflict(new ProblemDetails
            {
                Title  = "Conflict",
                Detail = ex.Message,
                Status = StatusCodes.Status409Conflict,
            });
        }
        catch (InvalidOperationException ex)
        {
            // EC-1 (self-deactivation) and other business rule violations → 422
            return UnprocessableEntity(new ProblemDetails
            {
                Title  = "Unprocessable Entity",
                Detail = ex.Message,
                Status = StatusCodes.Status422UnprocessableEntity,
            });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/admin/users/{id}/reactivate   (US_061 AC-4)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Restores a previously deactivated staff account to Active status.
    /// Previous role and permissions are preserved — no role membership changes are made (AC-4).
    /// </summary>
    [HttpPut("api/admin/users/{id}/reactivate")]
    [ProducesResponseType(typeof(AdminUserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ReactivateStaffAsync(
        [FromRoute] string id, CancellationToken ct)
    {
        _logger.LogInformation(
            "AdminUsers PUT reactivate user={UserId} by admin={AdminId}, CorrelationId={CorrelationId}",
            id, AdminUserId, HttpContext.TraceIdentifier);

        try
        {
            var updated = await _userService.ReactivateStaffAsync(id, AdminUserId, ct);
            return Ok(updated);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new ProblemDetails
            {
                Title  = "Not Found",
                Detail = $"User '{id}' was not found.",
                Status = StatusCodes.Status404NotFound,
            });
        }
        catch (InvalidOperationException ex)
        {
            return UnprocessableEntity(new ProblemDetails
            {
                Title  = "Unprocessable Entity",
                Detail = ex.Message,
                Status = StatusCodes.Status422UnprocessableEntity,
            });
        }
    }
}
