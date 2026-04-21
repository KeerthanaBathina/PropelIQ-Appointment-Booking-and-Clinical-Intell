using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.AI.NoShowRisk;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Coordinates no-show risk score calculation, persistence, and downstream integration
/// for appointment workflows (US_026, AIR-006, FR-014).
///
/// Responsibilities:
///   1. Call <see cref="INoShowRiskScoringService.ScoreAsync"/> to compute or refresh
///      the risk score for an appointment (AC-1, EC-1).
///   2. Persist the result to <c>appointments</c> risk metadata columns (task_003_db).
///   3. Return a <see cref="NoShowRiskScoreResult"/> to callers that need the live result.
///
/// Resilience contract:
///   - <strong>Never throws</strong>: scoring or persistence failures are swallowed and
///     logged as warnings so booking and queue workflows are never blocked (AC-4, AIR-O04).
///   - When scoring fails the appointment row is left with its previous (or null) metadata;
///     the caller receives <c>null</c> indicating scoring was skipped.
///
/// Callers:
///   - <see cref="AppointmentBookingService"/> — fire-and-forget after booking confirmed.
///   - <see cref="StaffDashboardController"/>  — on-demand refresh endpoint.
///   - <see cref="ArrivalQueueController"/>    — on-demand refresh endpoint.
///
/// Privacy (AIR-S01, NFR-017):
///   Only appointment UUID and score band are emitted in logs — no name, email, or DOB.
/// </summary>
public sealed class NoShowRiskOrchestrator
{
    private readonly INoShowRiskScoringService          _scoringService;
    private readonly ApplicationDbContext               _db;
    private readonly ILogger<NoShowRiskOrchestrator>    _logger;

    public NoShowRiskOrchestrator(
        INoShowRiskScoringService       scoringService,
        ApplicationDbContext            db,
        ILogger<NoShowRiskOrchestrator> logger)
    {
        _scoringService = scoringService;
        _db             = db;
        _logger         = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ScoreAndPersistAsync — main orchestration entry point
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calculates the no-show risk score for the given appointment and persists the
    /// latest metadata on the <c>appointments</c> row.
    ///
    /// Returns the score result so callers that need the live value (e.g. fire-and-forget
    /// post-booking hooks) do not need to re-query.  Returns <c>null</c> when scoring
    /// is skipped due to a failure (callers must handle null gracefully).
    ///
    /// This method resolves <paramref name="patientId"/> and <paramref name="appointmentTime"/>
    /// from the caller; the appointment row is updated in the same EF Core DbContext scope
    /// so the caller's subsequent SaveChanges is not required (the orchestrator calls it
    /// independently to avoid coupling save semantics).
    /// </summary>
    public async Task<NoShowRiskScoreResult?> ScoreAndPersistAsync(
        Guid              appointmentId,
        Guid              patientId,
        DateTime          appointmentTime,
        CancellationToken cancellationToken = default)
    {
        // ── 1. Compute risk score (never throws — returns error fallback) ──────
        NoShowRiskScoreResult result;
        try
        {
            result = await _scoringService.ScoreAsync(
                patientId, appointmentTime, cancellationToken);
        }
        catch (Exception ex)
        {
            // Defensive outer catch — INoShowRiskScoringService contract guarantees no throw
            // but a future refactor could break that, so we guard here too.
            _logger.LogWarning(ex,
                "NoShowRiskOrchestrator: unexpected error from scoring service — " +
                "skipping persistence for appointmentId={AppointmentId}.", appointmentId);
            return null;
        }

        // ── 2. Persist risk metadata ──────────────────────────────────────────
        try
        {
            await PersistRiskMetadataAsync(appointmentId, result, cancellationToken);
        }
        catch (Exception ex)
        {
            // Persistence failure must not block the calling workflow (AC-4).
            _logger.LogWarning(ex,
                "NoShowRiskOrchestrator: failed to persist risk metadata for " +
                "appointmentId={AppointmentId} — score will be recalculated on next request.",
                appointmentId);
            return null;
        }

        _logger.LogInformation(
            "NoShowRiskOrchestrator: appointmentId={AppointmentId}, band={Band}, " +
            "path={Path}, estimated={IsEstimated}.",
            appointmentId, result.Band, result.Path, result.IsEstimated);

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetOrScoreAsync — returns persisted score or computes fresh if absent
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the persisted score when available (EC-1 — reflects updated history),
    /// or calls <see cref="ScoreAndPersistAsync"/> to calculate a fresh score when the
    /// appointment has not been scored yet.
    ///
    /// Used by staff list endpoints to avoid redundant DB+scoring work on every render.
    /// </summary>
    public async Task<NoShowRiskScoreResult?> GetOrScoreAsync(
        Appointment       appointment,
        CancellationToken cancellationToken = default)
    {
        // Return the persisted result if already scored (EC-1)
        if (appointment.NoShowRiskScore.HasValue && appointment.RiskCalculatedAtUtc.HasValue)
        {
            return new NoShowRiskScoreResult
            {
                Score            = appointment.NoShowRiskScore.Value,
                Band             = appointment.NoShowRiskBand ?? NoShowRiskBand.Low,
                IsEstimated      = appointment.IsRiskEstimated ?? false,
                RequiresOutreach = appointment.RequiresOutreach ?? false,
                Path             = ScoringPath.Classification,
                ReasonCode       = "persisted",
            };
        }

        // Not yet scored — compute and persist now
        return await ScoreAndPersistAsync(
            appointment.Id,
            appointment.PatientId,
            appointment.AppointmentTime,
            cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes risk metadata columns on the appointment row and saves.
    /// Uses a tracked load (non-NoTracking) so EF Core can generate a minimal UPDATE.
    /// </summary>
    private async Task PersistRiskMetadataAsync(
        Guid                    appointmentId,
        NoShowRiskScoreResult   result,
        CancellationToken       cancellationToken)
    {
        var appointment = await _db.Appointments
            .FirstOrDefaultAsync(a => a.Id == appointmentId, cancellationToken);

        if (appointment is null)
        {
            _logger.LogWarning(
                "NoShowRiskOrchestrator: appointmentId={AppointmentId} not found — " +
                "skipping risk persistence.", appointmentId);
            return;
        }

        appointment.NoShowRiskScore      = result.Score;
        appointment.NoShowRiskBand       = result.Band;
        appointment.IsRiskEstimated      = result.IsEstimated;
        appointment.RequiresOutreach     = result.RequiresOutreach;
        appointment.RiskCalculatedAtUtc  = DateTime.UtcNow;
        appointment.UpdatedAt            = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);
    }
}
