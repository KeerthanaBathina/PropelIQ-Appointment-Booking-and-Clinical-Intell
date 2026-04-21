namespace UPACIP.Service.AI.NoShowRisk;

/// <summary>
/// Service contract for computing no-show risk scores (AIR-006, FR-014, US_026).
///
/// Contract guarantees:
///   - Never throws; always returns a valid <see cref="NoShowRiskScoreResult"/>.
///   - Scores fall within [0, 100] (guardrails enforced, EC-2).
///   - <c>IsEstimated</c> is true when insufficient history requires fallback rules (AC-3).
///   - <c>RequiresOutreach</c> is true when the score meets the high-risk threshold (EC-2).
///   - The same patientId + appointmentTime combination returns a consistent result
///     within a single request so downstream slot-swap consumers can depend on it (AC-4).
///
/// Scoring is synchronous-safe for the application tier: no external AI provider call
/// is made; the model is in-process (AIR Architecture — Classification pattern).
///
/// Implementation: <see cref="NoShowRiskScoringService"/>.
/// </summary>
public interface INoShowRiskScoringService
{
    /// <summary>
    /// Computes the no-show risk score for the specified patient and appointment.
    ///
    /// Never throws. On scoring failure returns an error-fallback result with
    /// <c>IsEstimated = true</c> and <c>Path = ErrorFallback</c> so callers are never blocked.
    /// </summary>
    /// <param name="patientId">Patient UUID.</param>
    /// <param name="appointmentTime">UTC timestamp of the appointment to score.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="NoShowRiskScoreResult"/> — never null.</returns>
    Task<NoShowRiskScoreResult> ScoreAsync(
        Guid              patientId,
        DateTime          appointmentTime,
        CancellationToken cancellationToken = default);
}
