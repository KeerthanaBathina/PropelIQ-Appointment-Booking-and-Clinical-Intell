using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UPACIP.Api.Authorization;
using UPACIP.Api.Models;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Notifications;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Admin-only endpoints for monitoring notification delivery reliability (US_037 AC-4, AC-1, AC-3).
///
/// Endpoints:
///   GET  /api/admin/notifications/logs       — paginated filtered log retrieval.
///   GET  /api/admin/notifications/summary    — aggregate delivery statistics.
///
/// Authorization: Admin role only (OWASP A01 — Broken Access Control).
///   Patient and Staff callers are rejected at the policy layer.
///
/// Statistics (GET /summary) follow EC-1:
///   <c>opted-out</c> and <c>cancelled-before-send</c> rows are excluded from
///   success/failure rate denominators so metrics reflect actual attempted deliveries.
///
/// All endpoints are paginated and filter-driven so they remain usable for future
/// admin UI screens without coupling to a specific layout.
/// </summary>
[ApiController]
[Route("api/admin/notifications")]
[Authorize(Policy = RbacPolicies.AdminOnly)]
[Produces("application/json")]
public sealed class AdminNotificationLogController : ControllerBase
{
    private readonly INotificationLogQueryService           _queryService;
    private readonly ILogger<AdminNotificationLogController> _logger;

    public AdminNotificationLogController(
        INotificationLogQueryService            queryService,
        ILogger<AdminNotificationLogController> logger)
    {
        _queryService = queryService;
        _logger       = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/admin/notifications/logs
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a paginated, filtered list of notification log records.
    ///
    /// Each row includes: recipient address, channel, notification type, current status,
    /// retry count, delivery timestamps, staff-review flag, contact-validation flag,
    /// and the count of per-attempt rows (AC-1).
    ///
    /// Query parameters (all optional):
    ///   <c>status</c>                    — filter by <see cref="NotificationStatus"/> value.
    ///   <c>channel</c>                   — filter by <see cref="DeliveryChannel"/> value.
    ///   <c>notificationType</c>          — filter by <see cref="NotificationType"/> value.
    ///   <c>staffReviewRequired</c>       — <c>true</c> to show only permanently-failed records.
    ///   <c>contactValidationRequired</c> — <c>true</c> to show only bounce-flagged records.
    ///   <c>from</c>                      — ISO 8601 UTC lower bound on CreatedAt.
    ///   <c>to</c>                        — ISO 8601 UTC upper bound on CreatedAt.
    ///   <c>page</c>                      — 1-based page index (default: 1).
    ///   <c>pageSize</c>                  — rows per page, capped at 200 (default: 50).
    /// </summary>
    [HttpGet("logs")]
    [ProducesResponseType(typeof(NotificationLogPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetLogs(
        [FromQuery] NotificationStatus? status                   = null,
        [FromQuery] DeliveryChannel?    channel                  = null,
        [FromQuery] NotificationType?   notificationType         = null,
        [FromQuery] bool?               staffReviewRequired      = null,
        [FromQuery] bool?               contactValidationRequired = null,
        [FromQuery] DateTime?           from                     = null,
        [FromQuery] DateTime?           to                       = null,
        [FromQuery] int                 page                     = 1,
        [FromQuery] int                 pageSize                 = 50,
        CancellationToken               cancellationToken        = default)
    {
        var filter = new NotificationLogFilterRequest(
            Status:                   status,
            Channel:                  channel,
            NotificationType:         notificationType,
            StaffReviewRequired:      staffReviewRequired,
            ContactValidationRequired: contactValidationRequired,
            From:                     from,
            To:                       to,
            Page:                     page,
            PageSize:                 pageSize);

        var result = await _queryService.GetPageAsync(filter, cancellationToken);

        _logger.LogInformation(
            "Admin notification log query: page={Page}, pageSize={PageSize}, total={Total}, " +
            "status={Status}, channel={Channel}, staffReview={StaffReview}.",
            page, pageSize, result.TotalCount, status, channel, staffReviewRequired);

        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/admin/notifications/summary
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns aggregate delivery statistics for the filtered notification log (AC-4).
    ///
    /// Metrics include:
    ///   - Total attempted, sent, failed, permanently failed, opted-out, cancelled-before-send.
    ///   - Success rate and failure rate percentages (attempted deliveries only — EC-1).
    ///   - Average delivery time in milliseconds from successful attempt-level records.
    ///   - Count of staff-review-pending records.
    ///
    /// Accepts the same filter query parameters as <c>GET /logs</c>.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(NotificationLogSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSummary(
        [FromQuery] NotificationStatus? status                   = null,
        [FromQuery] DeliveryChannel?    channel                  = null,
        [FromQuery] NotificationType?   notificationType         = null,
        [FromQuery] bool?               staffReviewRequired      = null,
        [FromQuery] bool?               contactValidationRequired = null,
        [FromQuery] DateTime?           from                     = null,
        [FromQuery] DateTime?           to                       = null,
        CancellationToken               cancellationToken        = default)
    {
        var filter = new NotificationLogFilterRequest(
            Status:                   status,
            Channel:                  channel,
            NotificationType:         notificationType,
            StaffReviewRequired:      staffReviewRequired,
            ContactValidationRequired: contactValidationRequired,
            From:                     from,
            To:                       to);

        var result = await _queryService.GetSummaryAsync(filter, cancellationToken);

        _logger.LogInformation(
            "Admin notification summary: attempted={Attempted}, sent={Sent}, " +
            "failed={Failed}, permanentlyFailed={PermanentlyFailed}, " +
            "successRate={SuccessRate}%, staffReviewPending={StaffReview}.",
            result.TotalAttempted, result.TotalSent, result.TotalFailed,
            result.TotalPermanentlyFailed, result.SuccessRatePct, result.StaffReviewPending);

        return Ok(result);
    }
}
