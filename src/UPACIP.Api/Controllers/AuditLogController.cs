using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using UPACIP.Api.Authorization;
using UPACIP.Api.Middleware;
using UPACIP.Api.Models;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Audit;
using UPACIP.Service.Auth;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Admin-only audit log query endpoint (US_064 AC-2, AC-3).
///
/// Endpoints:
///   GET /api/audit-logs  — Filtered, paginated audit log query with keyset cursor pagination.
///
/// Authorization (OWASP A01, NFR-011):
///   Requires the Admin role. Non-admin access returns 403 Forbidden.
///
/// Immutability enforcement (US_064 AC-2, FR-093, NFR-012):
///   Any non-GET request to /api/audit-logs is rejected with 405 Method Not Allowed.
///   The violation attempt is itself written to the audit log with action = DataModify / DataDelete.
///   This is enforced via <see cref="OnActionExecuting"/> which intercepts before any action body runs.
///
/// Pagination:
///   Cursor-based (keyset) pagination. Pass the <c>NextCursor</c> value from each response
///   as the <c>Cursor</c> query parameter on the next request. Supports traversal of millions
///   of rows with O(1) seek time (TR-013).
/// </summary>
[ApiController]
[Authorize(Policy = RbacPolicies.AdminOnly)]
[Produces("application/json")]
public sealed class AuditLogController : ControllerBase, IActionFilter
{
    private readonly IAuditLogQueryService       _queryService;
    private readonly IAuditLogService            _auditLogService;
    private readonly IClientInfoAccessor         _clientInfo;
    private readonly ILogger<AuditLogController> _logger;

    public AuditLogController(
        IAuditLogQueryService       queryService,
        IAuditLogService            auditLogService,
        IClientInfoAccessor         clientInfo,
        ILogger<AuditLogController> logger)
    {
        _queryService    = queryService;
        _auditLogService = auditLogService;
        _clientInfo      = clientInfo;
        _logger          = logger;
    }

    // ── Immutability guard (IActionFilter) ───────────────────────────────────────────────────

    /// <summary>
    /// Intercepts any non-GET request before the action body executes.
    /// Returns 405 and records the attempted violation as an audit entry (AC-2).
    /// </summary>
    void IActionFilter.OnActionExecuting(ActionExecutingContext context)
    {
        var method = context.HttpContext.Request.Method.ToUpperInvariant();
        if (method == "GET")
            return;

        // Determine which violation action to log.
        var violationAction = method == "DELETE"
            ? AuditAction.DataDelete
            : AuditAction.DataModify;

        var userId = GetCurrentUserId();

        // Fire-and-forget audit of the violation — do not await to avoid blocking the filter.
        // AuditLogService is fail-open so failure will not propagate.
        _ = _auditLogService.LogAsync(
            action:          violationAction,
            userId:          userId,
            resourceType:    "AuditLog",
            ipAddress:       _clientInfo.GetClientIpAddress(),
            userAgent:       _clientInfo.GetUserAgent(),
            cancellationToken: context.HttpContext.RequestAborted);

        _logger.LogWarning(
            "AuditLogController: rejected {Method} by UserId={UserId}. Immutability violation logged.",
            method, userId);

        context.Result = StatusCode(StatusCodes.Status405MethodNotAllowed, new
        {
            error   = "MethodNotAllowed",
            message = "Audit log entries are immutable and cannot be modified or deleted.",
        });
    }

    // IActionFilter requires both methods; OnActionExecuted is a no-op for this controller.
    void IActionFilter.OnActionExecuted(ActionExecutedContext context) { }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // GET /api/audit-logs
    // ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a filtered, paginated list of audit log entries.
    /// All filter parameters are optional — omit to retrieve all entries.
    /// Use <paramref name="cursor"/> (the <c>LogId</c> from the last item of the previous page)
    /// to fetch the next page efficiently.
    /// </summary>
    /// <param name="userId">Filter to entries authored by this user ID.</param>
    /// <param name="actionType">Filter to a specific <see cref="AuditAction"/> value.</param>
    /// <param name="entityType">Filter to a specific resource/entity type (e.g. "Patient").</param>
    /// <param name="entityId">Filter to a specific entity/resource ID.</param>
    /// <param name="startDate">Inclusive UTC lower bound on entry timestamp.</param>
    /// <param name="endDate">Inclusive UTC upper bound on entry timestamp.</param>
    /// <param name="cursor">Keyset cursor: the <c>LogId</c> of the last item from the previous page.</param>
    /// <param name="pageSize">Number of entries to return (default 50, max 200).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Paginated audit log entries returned successfully.</response>
    /// <response code="400">Invalid filter parameters.</response>
    /// <response code="401">Not authenticated.</response>
    /// <response code="403">Not authorised — Admin role required.</response>
    /// <response code="405">Non-GET method rejected — audit logs are immutable.</response>
    [HttpGet("api/audit-logs")]
    [ProducesResponseType(typeof(AuditLogQueryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse),         StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status405MethodNotAllowed)]
    public async Task<IActionResult> GetAuditLogs(
        [FromQuery] Guid?        userId      = null,
        [FromQuery] AuditAction? actionType  = null,
        [FromQuery] string?      entityType  = null,
        [FromQuery] Guid?        entityId    = null,
        [FromQuery] DateTime?    startDate   = null,
        [FromQuery] DateTime?    endDate     = null,
        [FromQuery] Guid?        cursor      = null,
        [FromQuery] int          pageSize    = 50,
        CancellationToken        ct          = default)
    {
        if (startDate.HasValue && endDate.HasValue && startDate.Value > endDate.Value)
            return BadRequest(BuildError(400, "startDate must not be later than endDate."));

        var request = new AuditLogQueryRequest
        {
            UserId      = userId,
            ActionType  = actionType,
            EntityType  = entityType,
            EntityId    = entityId,
            StartDate   = startDate,
            EndDate     = endDate,
            Cursor      = cursor,
            PageSize    = pageSize,
        };

        try
        {
            var result = await _queryService.QueryAsync(request, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AuditLogController: query failed for UserId={UserId} ActionType={ActionType}",
                userId, actionType);
            return StatusCode(StatusCodes.Status500InternalServerError,
                BuildError(500, "An error occurred while querying the audit log."));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────────────────────

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? User.FindFirstValue("sub");
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private ErrorResponse BuildError(int statusCode, string message)
    {
        var correlationId = HttpContext.Items.TryGetValue(CorrelationIdMiddleware.ItemsKey, out var v)
            ? v?.ToString() ?? Guid.NewGuid().ToString()
            : Guid.NewGuid().ToString();

        return new ErrorResponse
        {
            StatusCode    = statusCode,
            Message       = message,
            CorrelationId = correlationId,
            Timestamp     = DateTimeOffset.UtcNow,
        };
    }
}
