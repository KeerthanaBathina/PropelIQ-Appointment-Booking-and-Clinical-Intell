using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPACIP.Api.Authorization;
using UPACIP.Api.Models;
using UPACIP.Service.AgreementRate;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Admin-only REST endpoints for the AI-human coding agreement rate dashboard
/// (US_050, AC-1, AC-2, AC-3, AC-4, FR-067, FR-068, AIR-Q09).
///
/// Routes:
///   GET /api/coding/agreement-rate                          — Latest daily metric.
///   GET /api/coding/agreement-rate/history?from=&amp;to=    — Date-range history.
///   GET /api/coding/discrepancies?from=&amp;to=             — Discrepancy records.
///   GET /api/coding/agreement-rate/alerts                   — Below-threshold alerts.
///
/// Authorization (OWASP A01):
///   All endpoints require the <c>Admin</c> role (NFR-011, RBAC).
///   Patient and Staff callers are rejected at the policy layer.
///
/// Validation:
///   Date range exceeding 90 days returns 422 Unprocessable Entity (NFR-038).
///   Invalid <c>from</c> / <c>to</c> date strings return 400 Bad Request.
/// </summary>
[ApiController]
[Authorize(Policy = RbacPolicies.AdminOnly)]
[Produces("application/json")]
public sealed class AgreementRateController : ControllerBase
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    private const int MaxDateRangeDays = 90;

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly IAgreementRateService                _service;
    private readonly ILogger<AgreementRateController>     _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public AgreementRateController(
        IAgreementRateService            service,
        ILogger<AgreementRateController> logger)
    {
        _service = service;
        _logger  = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/coding/agreement-rate
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the most recently computed daily agreement-rate metric (US_050 AC-1, AC-2).
    ///
    /// When <c>meets_minimum_threshold</c> is <c>false</c> the caller must display
    /// "Not enough data" rather than the rate values (EC-1, requires 50+ verified codes).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("api/coding/agreement-rate")]
    [ProducesResponseType(typeof(AgreementRateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLatestAsync(CancellationToken ct)
    {
        var result = await _service.GetLatestMetricsAsync(ct);

        if (result is null)
        {
            _logger.LogInformation("AgreementRateController.GetLatest: no metric rows available yet.");
            return NotFound(new { message = "No agreement rate metrics available yet." });
        }

        return Ok(MapToDto(result));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/coding/agreement-rate/history
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns daily agreement-rate metrics for the given inclusive date range (US_050 AC-2).
    ///
    /// Results are ordered oldest-to-newest, suitable for charting a trend line.
    ///
    /// Returns 422 when the range exceeds 90 days (NFR-038).
    /// </summary>
    /// <param name="from">Start date (yyyy-MM-dd).</param>
    /// <param name="to">End date (yyyy-MM-dd).</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("api/coding/agreement-rate/history")]
    [ProducesResponseType(typeof(IReadOnlyList<AgreementRateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GetHistoryAsync(
        [FromQuery] DateOnly  from,
        [FromQuery] DateOnly  to,
        CancellationToken     ct)
    {
        if (to < from)
            return BadRequest(new { message = "'to' must be >= 'from'." });

        if ((to.DayNumber - from.DayNumber) > MaxDateRangeDays)
        {
            return UnprocessableEntity(new
            {
                message = $"Date range must not exceed {MaxDateRangeDays} days.",
            });
        }

        var results = await _service.GetMetricsRangeAsync(from, to, ct);
        return Ok(results.Select(MapToDto).ToList());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/coding/discrepancies
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns coding discrepancy records in the optional date range (US_050 AC-3, FR-068).
    ///
    /// Each record shows the AI-suggested code, the staff-selected replacement, the
    /// discrepancy classification (FullOverride / PartialOverride / MultipleCodes), and
    /// the clinical justification entered by staff.
    ///
    /// Returns 422 when the range exceeds 90 days (NFR-038).
    /// </summary>
    /// <param name="from">Optional start date (yyyy-MM-dd).</param>
    /// <param name="to">Optional end date (yyyy-MM-dd).</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("api/coding/discrepancies")]
    [ProducesResponseType(typeof(IReadOnlyList<CodingDiscrepancyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> GetDiscrepanciesAsync(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken     ct)
    {
        if (from.HasValue && to.HasValue)
        {
            if (to < from)
                return BadRequest(new { message = "'to' must be >= 'from'." });

            if ((to.Value.DayNumber - from.Value.DayNumber) > MaxDateRangeDays)
            {
                return UnprocessableEntity(new
                {
                    message = $"Date range must not exceed {MaxDateRangeDays} days.",
                });
            }
        }

        var results = await _service.GetDiscrepanciesAsync(from, to, ct);

        return Ok(results.Select(d => new CodingDiscrepancyDto
        {
            DiscrepancyId         = d.DiscrepancyId,
            PatientId             = d.PatientId,
            AiSuggestedCode       = d.AiSuggestedCode,
            StaffSelectedCode     = d.StaffSelectedCode,
            CodeType              = d.CodeType,
            DiscrepancyType       = d.DiscrepancyType,
            OverrideJustification = d.OverrideJustification,
            DetectedAt            = d.DetectedAt,
        }).ToList());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/coding/agreement-rate/alerts
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all below-threshold alert records ordered most-recent first (US_050 AC-4).
    ///
    /// An alert exists for every day where the agreement rate dropped below 98 %
    /// AND the minimum threshold of 50+ verified codes was met.
    /// Each alert includes the top 5 discrepancy-type patterns for that day.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("api/coding/agreement-rate/alerts")]
    [ProducesResponseType(typeof(IReadOnlyList<AgreementAlertDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAlertsAsync(CancellationToken ct)
    {
        var results = await _service.GetActiveAlertsAsync(ct);

        return Ok(results.Select(a => new AgreementAlertDto
        {
            AlertDate            = a.AlertDate,
            CurrentRate          = a.CurrentRate,
            DisagreementPatterns = a.DisagreementPatterns,
        }).ToList());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static AgreementRateDto MapToDto(AgreementRateResult r) =>
        new()
        {
            CalculationDate             = r.CalculationDate,
            DailyAgreementRate          = r.DailyAgreementRate,
            Rolling30DayRate            = r.Rolling30DayRate,
            TotalCodesVerified          = r.TotalCodesVerified,
            CodesApprovedWithoutOverride = r.CodesApprovedWithoutOverride,
            CodesOverridden             = r.CodesOverridden,
            CodesPartiallyOverridden    = r.CodesPartiallyOverridden,
            MeetsMinimumThreshold       = r.MeetsMinimumThreshold,
        };
}
