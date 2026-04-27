using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using UPACIP.Api.Authorization;
using UPACIP.Service.Admin;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Admin-only endpoints serving system metrics and rolling trend data for
/// the SCR-015 Admin Dashboard (US_058 AC-1, AC-2).
///
/// Routes:
///   GET /api/admin/metrics                          — Current metrics snapshot.
///   GET /api/admin/metrics/trends?period=7d|30d     — Rolling trend data.
///
/// Authorization (OWASP A01, NFR-011):
///   Both endpoints require the <c>Admin</c> role.
///   Staff and Patient callers receive 403 Forbidden.
///
/// Caching (NFR-030):
///   Results are cached in Redis with a 5-minute TTL by <see cref="ISystemMetricsService"/>.
///   On DB failure the last cached snapshot is returned with <c>IsStale = true</c>.
///
/// Logging (NFR-035):
///   Each request is logged with the admin user ID and correlation ID from
///   <see cref="HttpContext.TraceIdentifier"/> for audit trail purposes (NFR-012).
/// </summary>
[ApiController]
[Authorize(Policy = RbacPolicies.AdminOnly)]
[Produces("application/json")]
public sealed class AdminMetricsController : ControllerBase
{
    private static readonly string[] ValidPeriods = ["7d", "30d"];

    private readonly ISystemMetricsService              _metricsService;
    private readonly ILogger<AdminMetricsController>    _logger;

    public AdminMetricsController(
        ISystemMetricsService           metricsService,
        ILogger<AdminMetricsController> logger)
    {
        _metricsService = metricsService;
        _logger         = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/admin/metrics
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the current system metrics snapshot (US_058 AC-1).
    ///
    /// When live computation fails, returns the last cached values with
    /// <c>isStale = true</c> and a <c>generatedAt</c> timestamp indicating
    /// the age of the data ("Data as of [ts]" edge case).
    /// </summary>
    [HttpGet("api/admin/metrics")]
    [ProducesResponseType(typeof(AdminMetricsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCurrentMetricsAsync(CancellationToken ct)
    {
        var adminId      = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var correlationId = HttpContext.TraceIdentifier;

        _logger.LogInformation(
            "AdminMetrics accessed by user {AdminId} — CorrelationId={CorrelationId}.",
            adminId, correlationId);

        var result = await _metricsService.GetCurrentMetricsAsync(ct);
        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/admin/metrics/trends
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns rolling trend data over the requested period (US_058 AC-2).
    ///
    /// <paramref name="period"/> must be <c>7d</c> or <c>30d</c>;
    /// any other value returns 400 Bad Request (NFR-018).
    /// </summary>
    /// <param name="period">Rolling window — <c>7d</c> or <c>30d</c>.</param>
    [HttpGet("api/admin/metrics/trends")]
    [ProducesResponseType(typeof(AdminMetricsTrendsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetTrendsAsync(
        [FromQuery] string period = "7d",
        CancellationToken  ct     = default)
    {
        // ── Input validation — NFR-018 ────────────────────────────────────────
        if (!ValidPeriods.Contains(period, StringComparer.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "AdminMetrics trends: invalid period parameter '{Period}'.", period);

            return ValidationProblem(new ValidationProblemDetails
            {
                Title  = "Invalid period parameter.",
                Detail = $"The 'period' query parameter must be one of: {string.Join(", ", ValidPeriods)}.",
                Status = StatusCodes.Status400BadRequest,
                Errors = { ["period"] = [$"Must be one of: {string.Join(", ", ValidPeriods)}."] },
            });
        }

        var adminId      = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown";
        var correlationId = HttpContext.TraceIdentifier;

        _logger.LogInformation(
            "AdminMetrics trends ({Period}) accessed by user {AdminId} — CorrelationId={CorrelationId}.",
            period, adminId, correlationId);

        var days   = period == "30d" ? 30 : 7;
        var result = await _metricsService.GetTrendDataAsync(days, ct);
        return Ok(result);
    }
}
