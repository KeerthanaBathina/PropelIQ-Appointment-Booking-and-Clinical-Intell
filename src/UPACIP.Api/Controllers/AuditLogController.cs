using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using UPACIP.Api.Authorization;
using UPACIP.Api.Middleware;
using UPACIP.Api.Models;
using UPACIP.Contracts.Models;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Audit;
using UPACIP.Service.Auth;
using ContractsCommandService = UPACIP.Contracts.Services.IAuditLogCommandService;
using ContractsQueryService   = UPACIP.Contracts.Services.IAuditLogQueryService;
using Asp.Versioning;

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
[ApiVersion("1.0")]
[ApiController]
[Authorize(Policy = RbacPolicies.AdminOnly)]
[Produces("application/json")]
public sealed class AuditLogController : ControllerBase, IActionFilter
{
    private readonly IAuditLogQueryService       _queryService;
    private readonly IAuditLogService            _auditLogService;
    private readonly IClientInfoAccessor         _clientInfo;
    private readonly ILogger<AuditLogController> _logger;
    private readonly ContractsCommandService     _commandService;
    private readonly ContractsQueryService       _cqrsQueryService;

    public AuditLogController(
        IAuditLogQueryService       queryService,
        IAuditLogService            auditLogService,
        IClientInfoAccessor         clientInfo,
        ILogger<AuditLogController> logger,
        ContractsCommandService     commandService,
        ContractsQueryService       cqrsQueryService)
    {
        _queryService     = queryService;
        _auditLogService  = auditLogService;
        _clientInfo       = clientInfo;
        _logger           = logger;
        _commandService   = commandService;
        _cqrsQueryService = cqrsQueryService;
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

        // Allow POST to the command endpoint (system/admin audit entry creation — AC-3).
        var actionName = context.ActionDescriptor.RouteValues.TryGetValue("action", out var an) ? an : null;
        if (method == "POST" && actionName == "Create")
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

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // CQRS command endpoint (AC-3 write path through Service layer)
    // ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates an immutable audit log entry via the CQRS command path (AC-3).
    /// Restricted to Admin and System roles; intended for server-side service calls only.
    /// </summary>
    [HttpPost("api/audit-logs", Name = "CreateAuditLog")]
    [Authorize(Roles = "Admin,System")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Create(
        [FromBody] AuditLogEntry entry,
        CancellationToken ct)
    {
        try
        {
            var id = await _commandService.AppendAsync(entry, ct);
            return CreatedAtRoute("GetAuditLogById", new { id }, new { id });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(BuildError(400, ex.Message));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────
    // CQRS query endpoints (AC-3 read path via AuditLogReadDbContext)
    // ─────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a single audit log entry by its LogId (CQRS read path).
    /// </summary>
    [HttpGet("api/audit-logs/{id:guid}", Name = "GetAuditLogById")]
    [ProducesResponseType(typeof(AuditLogReadModel), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        var entry = await _cqrsQueryService.GetByIdAsync(id, ct);
        return entry is null ? NotFound() : Ok(entry);
    }

    /// <summary>
    /// Returns all audit log entries for a specific entity (CQRS read path, AC-3).
    /// Uses the IX_AuditLogs_Entity composite index.
    /// </summary>
    [HttpGet("api/audit-logs/entity/{entityType}/{entityId:guid}", Name = "GetAuditLogsByEntity")]
    [ProducesResponseType(typeof(List<AuditLogReadModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetByEntity(
        string entityType,
        Guid entityId,
        CancellationToken ct)
    {
        var entries = await _cqrsQueryService.GetByEntityAsync(entityType, entityId, ct);
        return Ok(entries);
    }

    /// <summary>
    /// Returns the count of audit log entries in a UTC date range (CQRS read path).
    /// Uses the ix_audit_logs_timestamp index for O(log n) range scans.
    /// </summary>
    [HttpGet("api/audit-logs/count", Name = "GetAuditLogCount")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Count(
        [FromQuery] DateTime fromUtc,
        [FromQuery] DateTime toUtc,
        CancellationToken ct)
    {
        if (fromUtc > toUtc)
            return BadRequest(BuildError(400, "fromUtc must not be later than toUtc."));

        var count = await _cqrsQueryService.CountByDateRangeAsync(fromUtc, toUtc, ct);
        return Ok(new { count, fromUtc, toUtc });
    }

    /// <summary>
    /// Filtered, paginated audit log query via the CQRS read path (AC-3).
    /// Uses AuditLogReadDbContext with NoTracking for compliance reporting.
    /// </summary>
    [HttpGet("api/audit-logs/v2", Name = "QueryAuditLogsV2")]
    [ProducesResponseType(typeof(PagedResult<AuditLogReadModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> QueryV2(
        [FromQuery] AuditLogQueryFilter filter,
        CancellationToken ct)
    {
        if (filter.FromUtc.HasValue && filter.ToUtc.HasValue && filter.FromUtc > filter.ToUtc)
            return BadRequest(BuildError(400, "FromUtc must not be later than ToUtc."));

        var result = await _cqrsQueryService.QueryAsync(filter, ct);
        return Ok(result);
    }
}
