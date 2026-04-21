using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.AI.NoShowRisk;

/// <summary>
/// Deterministic rule-based fallback scoring for patients with insufficient appointment
/// history or when the classification model path encounters an error (AC-3, AIR-O04).
///
/// Fallback rules:
///   1. Patients with zero prior appointments receive <c>NewPatientBaseScore</c> (30).
///   2. Patients with 1–2 prior appointments receive <c>DefaultBaseScore</c> (35) adjusted
///      by observed no-show and cancellation signals — partial history is used even if it
///      does not meet the full classification threshold.
///   3. When the scoring service encounters an unhandled exception the fallback returns a
///      neutral score to prevent booking/queue workflows from being blocked (AC-4, AIR-O04).
///
/// All fallback results set <c>IsEstimated = true</c> so the UI can display the "Est."
/// label (AC-3, task_001_fe_no_show_risk_display).
///
/// Output is capped and banded identically to the classification path so downstream
/// consumers cannot distinguish the code path from the score shape alone.
/// </summary>
public sealed class NoShowRiskFallbackPolicy
{
    // Configured defaults matching no-show-risk-config.json § fallback
    private const int DefaultBaseScore     = 35;
    private const int NewPatientBaseScore  = 30;

    // Guardrails matching no-show-risk-config.json § guardrails
    private const int MaxScore             = 100;
    private const int MinScore             = 0;
    private const int OutreachThreshold    = 70;

    // Minimum history threshold (AC-3)
    private const int MinHistoryAppointments = 3;

    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns true when the provided features do NOT meet the minimum history
    /// requirement for classification model scoring (AC-3).
    /// </summary>
    public static bool ShouldUseFallback(NoShowRiskFeatures features)
        => features.AppointmentCount < MinHistoryAppointments;

    /// <summary>
    /// Computes a rule-based estimated score from partial or missing patient history.
    ///
    /// Scoring logic:
    ///   - Start from base score (30 for new patients, 35 for 1–2 appointments).
    ///   - Add +30 if any observed no-shows exist (strong signal even with limited data).
    ///   - Add +10 if any observed cancellations exist.
    ///   - Clamp to [0, 100].
    ///
    /// Result is always <c>IsEstimated = true</c> and path = <c>RuleBasedFallback</c>.
    /// </summary>
    public NoShowRiskScoreResult ComputeFallbackScore(NoShowRiskFeatures features)
    {
        int baseScore = features.AppointmentCount == 0
            ? NewPatientBaseScore
            : DefaultBaseScore;

        // Apply observable partial-history signals even for new/low-history patients
        double rawScore = baseScore;

        if (features.NoShowRate > 0.0)
        {
            // Any observed no-show raises the estimated score significantly
            rawScore += 30.0 * features.NoShowRate;
        }

        if (features.CancellationRate > 0.0)
        {
            // Any observed cancellations add a smaller incremental penalty
            rawScore += 10.0 * features.CancellationRate;
        }

        int finalScore = Clamp((int)Math.Round(rawScore));

        return BuildResult(
            finalScore,
            isEstimated:  true,
            path:         ScoringPath.RuleBasedFallback,
            reasonCode:   features.AppointmentCount == 0
                ? "new_patient"
                : "insufficient_history");
    }

    /// <summary>
    /// Returns a safe neutral score when the scoring service encounters an
    /// unhandled exception (error fallback — AIR-O04, AC-4).
    ///
    /// The neutral score (30) prevents false high-risk classification in error
    /// conditions while still indicating estimated status to the UI.
    /// </summary>
    public NoShowRiskScoreResult ComputeErrorFallbackScore()
        => BuildResult(
            score:       NewPatientBaseScore,
            isEstimated: true,
            path:        ScoringPath.ErrorFallback,
            reasonCode:  "scoring_error");

    // ─────────────────────────────────────────────────────────────────────────
    // Shared helpers
    // ─────────────────────────────────────────────────────────────────────────

    internal static NoShowRiskScoreResult BuildResult(
        int         score,
        bool        isEstimated,
        ScoringPath path,
        string      reasonCode)
    {
        int clamped = Clamp(score);
        return new NoShowRiskScoreResult
        {
            Score            = clamped,
            Band             = ClassifyBand(clamped),
            IsEstimated      = isEstimated,
            RequiresOutreach = clamped >= OutreachThreshold,
            Path             = path,
            ReasonCode       = reasonCode,
        };
    }

    private static int Clamp(int score)
        => Math.Max(MinScore, Math.Min(MaxScore, score));

    private static NoShowRiskBand ClassifyBand(int score) => score switch
    {
        >= 70 => NoShowRiskBand.High,
        >= 30 => NoShowRiskBand.Medium,
        _     => NoShowRiskBand.Low,
    };
}
