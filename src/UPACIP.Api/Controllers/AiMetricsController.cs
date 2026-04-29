using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using UPACIP.Api.Authorization;
using UPACIP.Service.AiMetrics;
using UPACIP.Service.AiMetrics.Dtos;
using Asp.Versioning;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Admin-only REST endpoints for the AI accuracy monitoring dashboard
/// (US_072, AC-1 through AC-4).
///
/// <para>Routes:</para>
/// <list type="bullet">
///   <item><c>GET  /api/admin/ai-metrics/summary</c>          — Current metrics snapshot.</item>
///   <item><c>GET  /api/admin/ai-metrics/time-series</c>      — Time-series chart data.</item>
///   <item><c>GET  /api/admin/ai-metrics/alerts</c>           — Active (unacknowledged) alerts.</item>
///   <item><c>PUT  /api/admin/ai-metrics/alerts/{id}/acknowledge</c> — Acknowledge an alert.</item>
/// </list>
///
/// <para>Authorization (OWASP A01, NFR-011): all endpoints require the <c>Admin</c> role.</para>
///
/// <para>Validation: <c>startDate</c> must precede <c>endDate</c>; range must not exceed 365 days.</para>
/// </summary>
[ApiVersion("1.0")]
[ApiController]
[Authorize(Policy = RbacPolicies.AdminOnly)]
[Produces("application/json")]
public sealed class AiMetricsController : ControllerBase
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    private const int MaxDateRangeDays = 365;

    private static readonly HashSet<string> ValidGranularities =
        new(StringComparer.OrdinalIgnoreCase) { "daily", "weekly", "monthly" };

    private static readonly HashSet<string> ValidMetricTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "CodingAgreement",
            "ExtractionPrecision",
            "ExtractionRecall",
        };

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly IAiMetricsService             _service;
    private readonly ILogger<AiMetricsController>  _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public AiMetricsController(
        IAiMetricsService            service,
        ILogger<AiMetricsController> logger)
    {
        _service = service;
        _logger  = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/admin/ai-metrics/summary
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the current AI performance metrics summary for the monitoring dashboard
    /// (US_072 AC-1, AC-2, AC-3).
    ///
    /// When a metric's <c>SampleSize</c> is below <c>MinSampleSize</c> (30), the dashboard
    /// must display "Insufficient data" instead of the metric value (edge case).
    /// </summary>
    [HttpGet("api/admin/ai-metrics/summary")]
    [ProducesResponseType(typeof(AiMetricsSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSummaryAsync(CancellationToken ct)
    {
        _logger.LogInformation(
            "AiMetricsController.GetSummary: requested by admin {UserId}.",
            User.FindFirstValue(ClaimTypes.NameIdentifier));

        var summary = await _service.GetCurrentSummaryAsync(ct);
        return Ok(summary);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/admin/ai-metrics/time-series
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns time-series chart data for a single accuracy metric over a date range
    /// (US_072 edge case: daily/weekly/monthly views with date range selectors).
    /// </summary>
    /// <param name="metricType">
    /// One of: <c>CodingAgreement</c>, <c>ExtractionPrecision</c>, <c>ExtractionRecall</c>.
    /// </param>
    /// <param name="startDate">Inclusive range start (ISO 8601 date string, UTC).</param>
    /// <param name="endDate">Inclusive range end (ISO 8601 date string, UTC).</param>
    /// <param name="granularity">Aggregation: <c>daily</c> (default), <c>weekly</c>, <c>monthly</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("api/admin/ai-metrics/time-series")]
    [ProducesResponseType(typeof(AiMetricsTimeSeriesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GetTimeSeriesAsync(
        [FromQuery] string   metricType,
        [FromQuery] DateTime startDate,
        [FromQuery] DateTime endDate,
        [FromQuery] string   granularity = "daily",
        CancellationToken    ct          = default)
    {
        if (!ValidMetricTypes.Contains(metricType))
        {
            return BadRequest(new { error = $"Invalid metricType '{metricType}'. Valid values: CodingAgreement, ExtractionPrecision, ExtractionRecall." });
        }

        if (!ValidGranularities.Contains(granularity))
        {
            return BadRequest(new { error = $"Invalid granularity '{granularity}'. Valid values: daily, weekly, monthly." });
        }

        if (startDate >= endDate)
        {
            return UnprocessableEntity(new { error = "startDate must be before endDate." });
        }

        if ((endDate - startDate).TotalDays > MaxDateRangeDays)
        {
            return UnprocessableEntity(new { error = $"Date range must not exceed {MaxDateRangeDays} days." });
        }

        _logger.LogInformation(
            "AiMetricsController.GetTimeSeries: metric={Metric}, start={Start:yyyy-MM-dd}, end={End:yyyy-MM-dd}, granularity={Granularity}.",
            metricType, startDate, endDate, granularity);

        var result = await _service.GetTimeSeriesAsync(
            metricType, startDate.ToUniversalTime(), endDate.ToUniversalTime(), granularity, ct);

        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/admin/ai-metrics/alerts
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all active (unacknowledged) AI metric alerts (US_072 AC-4).
    /// </summary>
    [HttpGet("api/admin/ai-metrics/alerts")]
    [ProducesResponseType(typeof(IReadOnlyList<AiMetricAlertDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAlertsAsync(CancellationToken ct)
    {
        var alerts = await _service.GetActiveAlertsAsync(ct);
        return Ok(alerts);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/admin/ai-metrics/alerts/{id}/acknowledge
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Acknowledges an AI metric alert, marking it as reviewed by the requesting admin
    /// (US_072 AC-4).
    /// </summary>
    /// <param name="id">The <c>AlertId</c> (UUID) of the alert to acknowledge.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPut("api/admin/ai-metrics/alerts/{id:guid}/acknowledge")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AcknowledgeAlertAsync(Guid id, CancellationToken ct)
    {
        var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(userIdClaim, out Guid userId))
        {
            return Unauthorized();
        }

        bool updated = await _service.AcknowledgeAlertAsync(id, userId, ct);

        if (!updated)
        {
            return NotFound(new { error = $"Alert {id} not found." });
        }

        _logger.LogInformation(
            "AiMetricsController.AcknowledgeAlert: alert {AlertId} acknowledged by admin {UserId}.",
            id, userId);

        return NoContent();
    }
}
