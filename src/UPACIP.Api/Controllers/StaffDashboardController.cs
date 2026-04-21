using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UPACIP.Api.Authorization;
using UPACIP.Api.Middleware;
using UPACIP.Api.Models;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Appointments;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Staff-only endpoints exposing today's appointment schedule with no-show risk metadata
/// for the SCR-010 Staff Dashboard screen (US_026, US_057, AC-2).
///
/// Endpoints:
///   GET  /api/staff/appointments/today           — today's schedule with risk scores.
///   POST /api/staff/appointments/{id}/refresh-risk — on-demand risk score refresh.
///
/// Authorization: Staff or Admin role only.
///   Patient callers are rejected at the policy layer (OWASP A01 — Broken Access Control).
///
/// Performance (NFR-005):
///   Single EF Core projection query with AsNoTracking for the list; risk metadata is read
///   from persisted columns so no live re-scoring occurs on every list render.
///
/// Privacy (AIR-S01, NFR-017):
///   Patient name/email is returned for display purposes only (staff operational need).
///   Raw patient history counts are never included in responses.
/// </summary>
[ApiController]
[Route("api/staff/appointments")]
[Authorize(Policy = RbacPolicies.StaffOrAdmin)]
[Produces("application/json")]
public sealed class StaffDashboardController : ControllerBase
{
    private readonly ApplicationDbContext               _db;
    private readonly NoShowRiskOrchestrator             _riskOrchestrator;
    private readonly ILogger<StaffDashboardController>  _logger;

    public StaffDashboardController(
        ApplicationDbContext                db,
        NoShowRiskOrchestrator              riskOrchestrator,
        ILogger<StaffDashboardController>   logger)
    {
        _db               = db;
        _riskOrchestrator = riskOrchestrator;
        _logger           = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/staff/appointments/today
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns today's scheduled appointments with persisted no-show risk metadata.
    ///
    /// Response includes: appointment fields, patient display name, risk score, band,
    /// estimated flag, and outreach indicator for each row (AC-2, SCR-010).
    ///
    /// Appointments not yet scored return null risk fields — the frontend renders "N/A".
    ///
    /// Query parameters:
    ///   <c>date</c> — optional ISO date (yyyy-MM-dd); defaults to today UTC.
    /// </summary>
    [HttpGet("today")]
    [ProducesResponseType(typeof(StaffScheduleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetTodaySchedule(
        [FromQuery] string? date,
        CancellationToken   cancellationToken)
    {
        DateOnly targetDate;
        if (!string.IsNullOrEmpty(date) && DateOnly.TryParse(date, out var parsed))
            targetDate = parsed;
        else
            targetDate = DateOnly.FromDateTime(DateTime.UtcNow);

        var startUtc = targetDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endUtc   = targetDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var appointments = await _db.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Where(a =>
                a.AppointmentTime >= startUtc &&
                a.AppointmentTime <= endUtc   &&
                a.Status          != AppointmentStatus.Cancelled &&
                a.Patient.DeletedAt == null)
            .OrderBy(a => a.AppointmentTime)
            .ThenBy(a => a.NoShowRiskScore.HasValue ? 0 : 1)  // scored rows first
            .ThenByDescending(a => a.NoShowRiskScore)          // highest risk first within scored rows
            .Select(a => new StaffAppointmentRiskDto
            {
                AppointmentId    = a.Id,
                BookingReference = a.BookingReference,
                AppointmentTime  = a.AppointmentTime,
                ProviderName     = a.ProviderName,
                AppointmentType  = a.AppointmentType,
                Status           = a.Status.ToString(),
                IsWalkIn         = a.IsWalkIn,
                NoShowRiskScore  = a.NoShowRiskScore,
                RiskBand         = a.NoShowRiskBand,
                IsEstimated      = a.IsRiskEstimated,
                RequiresOutreach = a.RequiresOutreach,
            })
            .ToListAsync(cancellationToken);

        var highRiskCount = appointments.Count(a => a.RequiresOutreach == true);

        _logger.LogInformation(
            "StaffDashboard.GetTodaySchedule: date={Date}, count={Count}, highRisk={HighRisk}.",
            targetDate, appointments.Count, highRiskCount);

        return Ok(new StaffScheduleResponse(appointments, targetDate.ToString("yyyy-MM-dd"), highRiskCount));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/staff/appointments/{id}/refresh-risk
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Triggers an on-demand risk score refresh for a single appointment (EC-1).
    ///
    /// Used when staff suspects the score is stale after a patient's history changed
    /// (e.g. a completed visit was just recorded for that patient).
    ///
    /// Returns 200 OK with the refreshed risk metadata.
    /// Returns 404 Not Found when the appointment does not exist.
    /// Returns 200 OK with the previous score when scoring fails (never blocks workflow, AC-4).
    /// </summary>
    [HttpPost("{appointmentId:guid}/refresh-risk")]
    [ProducesResponseType(typeof(StaffAppointmentRiskDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RefreshRisk(
        Guid              appointmentId,
        CancellationToken cancellationToken)
    {
        var appointment = await _db.Appointments
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);

        if (appointment is null)
            return NotFound(BuildError(StatusCodes.Status404NotFound,
                $"Appointment {appointmentId} not found."));

        var result = await _riskOrchestrator.ScoreAndPersistAsync(
            appointment.Id,
            appointment.PatientId,
            appointment.AppointmentTime,
            cancellationToken);

        // Re-load persisted state so the response reflects what was written to DB
        var updated = await _db.Appointments
            .AsNoTracking()
            .FirstAsync(a => a.Id == appointmentId, cancellationToken);

        _logger.LogInformation(
            "StaffDashboard.RefreshRisk: appointmentId={AppointmentId}, " +
            "band={Band}, path={Path}.",
            appointmentId, result?.Band.ToString() ?? "none", result?.Path.ToString() ?? "error");

        return Ok(new StaffAppointmentRiskDto
        {
            AppointmentId    = updated.Id,
            BookingReference = updated.BookingReference,
            AppointmentTime  = updated.AppointmentTime,
            ProviderName     = updated.ProviderName,
            AppointmentType  = updated.AppointmentType,
            Status           = updated.Status.ToString(),
            IsWalkIn         = updated.IsWalkIn,
            NoShowRiskScore  = updated.NoShowRiskScore,
            RiskBand         = updated.NoShowRiskBand,
            IsEstimated      = updated.IsRiskEstimated,
            RequiresOutreach = updated.RequiresOutreach,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private ErrorResponse BuildError(int statusCode, string message)
    {
        var correlationId = HttpContext.Items[CorrelationIdMiddleware.ItemsKey]?.ToString()
                            ?? Guid.NewGuid().ToString();
        return new ErrorResponse
        {
            StatusCode    = statusCode,
            Message       = message,
            CorrelationId = correlationId,
            Timestamp     = DateTimeOffset.UtcNow,
        };
    }
}

/// <summary>
/// Response envelope for <c>GET /api/staff/appointments/today</c>.
/// </summary>
public sealed record StaffScheduleResponse(
    IReadOnlyList<StaffAppointmentRiskDto> Appointments,
    string                                 ScheduleDate,
    int                                    HighRiskCount);
