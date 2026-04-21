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
/// Staff-only endpoints for the arrival queue view (SCR-011) with no-show risk metadata
/// (US_026, US_053, AC-2, EC-2).
///
/// Endpoints:
///   GET  /api/staff/queue           — today's arrival queue with risk scores + wait times.
///   POST /api/staff/queue/{id}/refresh-risk — on-demand risk score refresh for a queue row.
///
/// Authorization: Staff or Admin role only.
///
/// Performance (NFR-005):
///   Single EF Core join query (AsNoTracking) projecting queue + appointment + risk columns.
///   Wait time is computed server-side as (UtcNow - ArrivalTimestamp).TotalMinutes.
///
/// Queue ordering: by arrival timestamp (oldest first) within the same priority band;
///   Urgent entries are sorted before Normal entries regardless of arrival time.
/// </summary>
[ApiController]
[Route("api/staff/queue")]
[Authorize(Policy = RbacPolicies.StaffOrAdmin)]
[Produces("application/json")]
public sealed class ArrivalQueueController : ControllerBase
{
    private readonly ApplicationDbContext               _db;
    private readonly NoShowRiskOrchestrator             _riskOrchestrator;
    private readonly ILogger<ArrivalQueueController>    _logger;

    public ArrivalQueueController(
        ApplicationDbContext                db,
        NoShowRiskOrchestrator              riskOrchestrator,
        ILogger<ArrivalQueueController>     logger)
    {
        _db               = db;
        _riskOrchestrator = riskOrchestrator;
        _logger           = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/staff/queue
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns today's waiting and in-visit queue entries with no-show risk metadata.
    ///
    /// Each row contains appointment fields, patient display name, persisted risk score,
    /// risk band, estimated flag, outreach indicator, and computed wait time (SCR-011 AC-2, EC-2).
    ///
    /// Completed queue entries are excluded — they are shown in the schedule history view.
    /// Rows are ordered: Urgent first, then by arrival timestamp ascending.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ArrivalQueueResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetQueue(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var todayStart = now.Date;
        var todayEnd   = todayStart.AddDays(1);

        var entries = await _db.QueueEntries
            .AsNoTracking()
            .Include(q => q.Appointment)
                .ThenInclude(a => a.Patient)
            .Where(q =>
                q.Status != QueueStatus.Completed &&
                q.ArrivalTimestamp >= todayStart   &&
                q.ArrivalTimestamp <  todayEnd     &&
                q.Appointment.Patient.DeletedAt == null)
            .OrderByDescending(q => q.Priority == QueuePriority.Urgent)  // Urgent first
            .ThenBy(q => q.ArrivalTimestamp)                               // oldest arrival first
            .ToListAsync(cancellationToken);

        var rows = entries.Select((q, index) => new StaffAppointmentRiskDto
        {
            AppointmentId    = q.Appointment.Id,
            BookingReference = q.Appointment.BookingReference,
            AppointmentTime  = q.Appointment.AppointmentTime,
            ProviderName     = q.Appointment.ProviderName,
            AppointmentType  = q.Appointment.AppointmentType,
            Status           = q.Appointment.Status.ToString(),
            IsWalkIn         = q.Appointment.IsWalkIn,
            NoShowRiskScore  = q.Appointment.NoShowRiskScore,
            RiskBand         = q.Appointment.NoShowRiskBand,
            IsEstimated      = q.Appointment.IsRiskEstimated,
            RequiresOutreach = q.Appointment.RequiresOutreach,
            QueuePosition    = index + 1,
            WaitMinutes      = (int)(now - q.ArrivalTimestamp).TotalMinutes,
        }).ToList();

        var outreachCount = rows.Count(r => r.RequiresOutreach == true);

        _logger.LogInformation(
            "ArrivalQueue.GetQueue: queueSize={Count}, outreachRequired={Outreach}.",
            rows.Count, outreachCount);

        return Ok(new ArrivalQueueResponse(rows, outreachCount));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/staff/queue/{appointmentId}/refresh-risk
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Triggers an on-demand risk score refresh for a queue row (EC-1).
    ///
    /// Returns 200 OK with the refreshed metadata row.
    /// Returns 404 when no active queue entry exists for the appointment.
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
        var queueEntry = await _db.QueueEntries
            .AsNoTracking()
            .Include(q => q.Appointment)
            .FirstOrDefaultAsync(
                q => q.AppointmentId == appointmentId &&
                     q.Status        != QueueStatus.Completed,
                cancellationToken);

        if (queueEntry is null)
            return NotFound(BuildError(StatusCodes.Status404NotFound,
                $"No active queue entry found for appointment {appointmentId}."));

        var result = await _riskOrchestrator.ScoreAndPersistAsync(
            queueEntry.Appointment.Id,
            queueEntry.Appointment.PatientId,
            queueEntry.Appointment.AppointmentTime,
            cancellationToken);

        // Re-load persisted state
        var updated = await _db.Appointments
            .AsNoTracking()
            .FirstAsync(a => a.Id == appointmentId, cancellationToken);

        var now = DateTime.UtcNow;

        _logger.LogInformation(
            "ArrivalQueue.RefreshRisk: appointmentId={AppointmentId}, " +
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
            WaitMinutes      = (int)(now - queueEntry.ArrivalTimestamp).TotalMinutes,
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
/// Response envelope for <c>GET /api/staff/queue</c>.
/// </summary>
public sealed record ArrivalQueueResponse(
    IReadOnlyList<StaffAppointmentRiskDto> Queue,
    int                                    OutreachRequiredCount);
