using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.AI.NoShowRisk;

/// <summary>
/// Builds the normalized feature vector used by <see cref="NoShowRiskScoringService"/>
/// to compute an in-process no-show risk score (AIR-006, AC-1).
///
/// Features extracted per AC-1:
///   - No-show rate (prior no-shows / total completed+no-show appointments)
///   - Cancellation rate (prior cancellations / total historical appointments)
///   - Appointment count (used to detect insufficient history — AC-3)
///   - Time-of-day bucket (earlyMorning / morning / midday / afternoon / lateAfternoon)
///   - Day-of-week
///
/// EC-1: Features are read fresh from the database on every call so a completed visit
/// immediately reflects in the next scoring request without manual retraining.
///
/// Privacy (AIR-S01, NFR-017):
///   - Only aggregate counts and appointment metadata are read — no name, DOB, or other
///     patient identifiers are included in or logged as part of feature values.
/// </summary>
public sealed class NoShowRiskFeatureExtractor
{
    private readonly ApplicationDbContext                       _db;
    private readonly ILogger<NoShowRiskFeatureExtractor>        _logger;

    public NoShowRiskFeatureExtractor(
        ApplicationDbContext                    db,
        ILogger<NoShowRiskFeatureExtractor>     logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <summary>
    /// Extracts features for the specified patient and appointment time.
    ///
    /// Returns <see cref="NoShowRiskFeatures"/> containing all normalized inputs
    /// and the raw appointment count so the caller can decide whether to invoke the
    /// classification model or fall back to rule-based scoring (AC-3).
    /// </summary>
    /// <param name="patientId">Patient UUID whose history is queried.</param>
    /// <param name="appointmentTime">UTC timestamp of the candidate appointment (used for time/day features).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<NoShowRiskFeatures> ExtractAsync(
        Guid              patientId,
        DateTime          appointmentTime,
        CancellationToken cancellationToken = default)
    {
        // ── 1. Load historical appointment counts (EC-1: always fresh from DB) ─
        // Aggregate in the database — only counts are returned to the application layer.
        var historyCounts = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.PatientId == patientId)
            .GroupBy(_ => true)
            .Select(g => new
            {
                Total        = g.Count(),
                NoShows      = g.Count(a => a.Status == AppointmentStatus.NoShow),
                Cancellations = g.Count(a => a.Status == AppointmentStatus.Cancelled),
                Completed    = g.Count(a => a.Status == AppointmentStatus.Completed),
            })
            .FirstOrDefaultAsync(cancellationToken);

        int total         = historyCounts?.Total        ?? 0;
        int noShows       = historyCounts?.NoShows      ?? 0;
        int cancellations = historyCounts?.Cancellations ?? 0;
        int completed     = historyCounts?.Completed    ?? 0;

        _logger.LogDebug(
            "FeatureExtractor: patientId={PatientId} — total={Total}, noShows={NoShows}, " +
            "cancellations={Cancellations}, completed={Completed}.",
            patientId, total, noShows, cancellations, completed);

        // ── 2. Derive normalized rates ────────────────────────────────────────
        // Denominator uses only outcome-bearing appointments (no-show or completed)
        // to avoid diluting the rate with scheduled-but-not-yet-occurred appointments.
        int outcomeBearing = noShows + completed;
        double noShowRate         = outcomeBearing > 0 ? (double)noShows / outcomeBearing : 0.0;
        double cancellationRate   = total > 0 ? (double)cancellations / total : 0.0;

        // ── 3. Time-of-day bucket ─────────────────────────────────────────────
        var localHour   = appointmentTime.Hour;  // stored in UTC; treated as-is for bucketing
        var timeOfDay   = ClassifyTimeOfDay(localHour);

        // ── 4. Day-of-week ───────────────────────────────────────────────────
        var dayOfWeek = appointmentTime.DayOfWeek;

        return new NoShowRiskFeatures(
            PatientId:         patientId,
            AppointmentCount:  total,
            NoShowRate:        noShowRate,
            CancellationRate:  cancellationRate,
            TimeOfDay:         timeOfDay,
            DayOfWeek:         dayOfWeek);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static TimeOfDayBucket ClassifyTimeOfDay(int hour) => hour switch
    {
        >= 6  and <= 8  => TimeOfDayBucket.EarlyMorning,
        >= 9  and <= 11 => TimeOfDayBucket.Morning,
        >= 12 and <= 13 => TimeOfDayBucket.Midday,
        >= 14 and <= 16 => TimeOfDayBucket.Afternoon,
        _               => TimeOfDayBucket.LateAfternoon,
    };
}

// ─────────────────────────────────────────────────────────────────────────────
// Value objects
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Extracted feature vector for a single scoring request.
/// All numeric fields are already normalized to [0, 1] ranges.
/// </summary>
public sealed record NoShowRiskFeatures(
    /// <summary>Patient UUID — used only for audit logging; never included in feature values.</summary>
    Guid        PatientId,
    /// <summary>Total appointments in patient history. Used to determine fallback eligibility (AC-3).</summary>
    int         AppointmentCount,
    /// <summary>Historical no-show rate [0, 1].</summary>
    double      NoShowRate,
    /// <summary>Historical cancellation rate [0, 1].</summary>
    double      CancellationRate,
    /// <summary>Time-of-day bucket for the candidate appointment.</summary>
    TimeOfDayBucket TimeOfDay,
    /// <summary>Day-of-week for the candidate appointment.</summary>
    DayOfWeek   DayOfWeek);

/// <summary>
/// Time-of-day buckets matching the ranges defined in <c>no-show-risk-config.json</c>.
/// </summary>
public enum TimeOfDayBucket
{
    EarlyMorning,   // 06:00–08:59
    Morning,        // 09:00–11:59
    Midday,         // 12:00–13:59
    Afternoon,      // 14:00–16:59
    LateAfternoon,  // 17:00+
}
