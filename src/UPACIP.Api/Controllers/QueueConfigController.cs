using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UPACIP.Api.Authorization;
using UPACIP.Api.Middleware;
using UPACIP.Api.Models;
using UPACIP.Service.Queue;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Admin endpoints for managing queue configuration (US_055 AC-3).
///
/// Endpoints:
///   GET  /api/queue/config/threshold   — returns the current wait time alert threshold (any staff/admin)
///   PUT  /api/queue/config/threshold   — updates the threshold (admin-only)
///
/// The threshold is stored in Redis (60-second TTL) and falls back to the appsettings
/// <c>QueueSettings:WaitTimeThresholdMinutes</c> default (30 minutes) when no override is cached.
///
/// GET is accessible to Staff and Admin so the frontend polling hook (<c>useWaitThreshold</c>)
/// works for all queue dashboard users. PUT is restricted to Admin role (AC-3).
/// </summary>
[ApiController]
[Route("api/queue/config")]
[Authorize(Policy = RbacPolicies.StaffOrAdmin)]
[Produces("application/json")]
public sealed class QueueConfigController : ControllerBase
{
    private readonly IQueueService                _queueService;
    private readonly ILogger<QueueConfigController> _logger;

    public QueueConfigController(
        IQueueService                    queueService,
        ILogger<QueueConfigController>   logger)
    {
        _queueService = queueService;
        _logger       = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/queue/config/threshold
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the current wait time alert threshold in minutes.
    /// Read from Redis cache (60-second TTL) with appsettings fallback (default: 30 min).
    /// Polled every 30 seconds by the frontend <c>useWaitThreshold</c> hook (AC-3).
    /// </summary>
    [HttpGet("threshold")]
    [ProducesResponseType(typeof(WaitThresholdConfigResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetThreshold(CancellationToken cancellationToken)
    {
        var minutes = await _queueService.GetWaitThresholdAsync(cancellationToken);

        _logger.LogDebug(
            "QueueConfigController.GetThreshold: returning {Minutes} min.", minutes);

        return Ok(new WaitThresholdConfigResponse { ThresholdMinutes = minutes });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/queue/config/threshold
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Updates the wait time alert threshold. Admin role required (AC-3).
    /// Immediately invalidates the queue cache so all active views recompute alert levels
    /// on their next 5-second poll. Audit-logged with <c>WaitThresholdConfigChanged</c> action.
    /// Valid range: 5–120 minutes.
    /// </summary>
    [HttpPut("threshold")]
    [Authorize(Policy = RbacPolicies.AdminOnly)]
    [ProducesResponseType(typeof(WaitThresholdConfigResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateThreshold(
        [FromBody] UpdateWaitThresholdRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var adminUserId   = ResolveAdminUserId();
        var correlationId = GetCorrelationId();

        await _queueService.UpdateWaitThresholdAsync(
            request.ThresholdMinutes, adminUserId, correlationId, cancellationToken);

        _logger.LogInformation(
            "QueueConfigController.UpdateThreshold: {Minutes} min set by adminUserId={AdminUserId}, " +
            "correlationId={CorrelationId}.",
            request.ThresholdMinutes, adminUserId, correlationId);

        return Ok(new WaitThresholdConfigResponse { ThresholdMinutes = request.ThresholdMinutes });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private Guid ResolveAdminUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    private string GetCorrelationId()
        => HttpContext.Items.TryGetValue(CorrelationIdMiddleware.ItemsKey, out var id)
            ? id?.ToString() ?? string.Empty
            : string.Empty;

    private ErrorResponse BuildError(int statusCode, string message)
        => new()
        {
            StatusCode    = statusCode,
            Message       = message,
            CorrelationId = GetCorrelationId(),
            Timestamp     = DateTimeOffset.UtcNow,
        };
}
