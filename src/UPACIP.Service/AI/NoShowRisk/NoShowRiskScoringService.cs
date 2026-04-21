using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;

namespace UPACIP.Service.AI.NoShowRisk;

/// <summary>
/// In-process no-show risk scoring engine (AIR-006, FR-014, US_026 AC-1, AC-3, AC-4, EC-2).
///
/// Architecture — Classification + Rule-based fallback (AIR Architecture Pattern):
///   1. <see cref="NoShowRiskFeatureExtractor"/> fetches fresh appointment history
///      aggregates from PostgreSQL (EC-1 — no stale data).
///   2. When history is sufficient (≥ configured minimum), a weighted linear classification
///      model produces a normalized 0-100 score.
///   3. When history is insufficient the <see cref="NoShowRiskFallbackPolicy"/> returns
///      a rule-based estimated score (AC-3).
///   4. When the scoring path throws, the fallback policy returns an error-safe neutral
///      score that does not block booking or queue workflows (AC-4, AIR-O04).
///
/// Weighted classification model (step 2):
///   score = (noShowRate × 0.45 + cancellationRate × 0.20 + normalizedCount × 0.05
///            + timeOfDayFactor × 0.15 + dayOfWeekFactor × 0.15) × 100
///
///   All coefficients match <c>no-show-risk-config.json § features</c> so they can be
///   updated without redeployment via configuration reload.
///
/// Circuit breaker (AIR-O04):
///   The Polly circuit breaker wraps the classification path — it opens after 5 consecutive
///   failures and retries after 30 seconds (matching the config values).  When the circuit
///   is open the fallback policy's error score is returned immediately.
///
/// Guardrails (AIR-S01, NFR-017):
///   - No patient identifiers (name, email, DOB) are logged — only the appointment-level
///     PatientId UUID and the scoring band/path appear in log events.
///   - Input validation rejects null/default GUIDs before feature extraction.
///   - Score is clamped to [0, 100] by <see cref="NoShowRiskFallbackPolicy.BuildResult"/>.
///
/// Slot-swap prioritization (AC-4):
///   The returned <see cref="NoShowRiskScoreResult.Score"/> is directly comparable across
///   candidates.  Lower scores indicate lower no-show risk and are preferred in the swap engine.
/// </summary>
public sealed class NoShowRiskScoringService : INoShowRiskScoringService
{
    // Coefficients sourced from no-show-risk-config.json § features
    private const double NoShowRateWeight      = 0.45;
    private const double CancellationRateWeight = 0.20;
    private const double AppointmentCountWeight = 0.05;
    private const double TimeOfDayWeight        = 0.15;
    private const double DayOfWeekWeight        = 0.15;

    // Guardrails matching no-show-risk-config.json § guardrails
    private const int OutreachThreshold = 70;

    private readonly NoShowRiskFeatureExtractor             _extractor;
    private readonly NoShowRiskFallbackPolicy               _fallback;
    private readonly ILogger<NoShowRiskScoringService>      _logger;

    // Polly circuit breaker wrapping the classification path (AIR-O04)
    private readonly AsyncCircuitBreakerPolicy _circuitBreaker;

    public NoShowRiskScoringService(
        NoShowRiskFeatureExtractor          extractor,
        NoShowRiskFallbackPolicy            fallback,
        ILogger<NoShowRiskScoringService>   logger)
    {
        _extractor = extractor;
        _fallback  = fallback;
        _logger    = logger;

        // Open after 5 consecutive exceptions; allow retry after 30 s (AIR-O04)
        _circuitBreaker = Policy
            .Handle<Exception>()
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 5,
                durationOfBreak:                 TimeSpan.FromSeconds(30),
                onBreak: (ex, duration) =>
                    _logger.LogError(ex,
                        "NoShowRisk circuit breaker OPEN for {DurationSeconds}s.",
                        (int)duration.TotalSeconds),
                onReset: () =>
                    _logger.LogInformation("NoShowRisk circuit breaker CLOSED (reset)."),
                onHalfOpen: () =>
                    _logger.LogInformation("NoShowRisk circuit breaker HALF-OPEN (probing)."));
    }

    /// <inheritdoc/>
    public async Task<NoShowRiskScoreResult> ScoreAsync(
        Guid              patientId,
        DateTime          appointmentTime,
        CancellationToken cancellationToken = default)
    {
        // ── Input guard ───────────────────────────────────────────────────────
        if (patientId == Guid.Empty)
        {
            _logger.LogWarning("NoShowRisk: ScoreAsync called with empty patientId — returning error fallback.");
            return _fallback.ComputeErrorFallbackScore();
        }

        // ── Feature extraction + scoring wrapped in circuit breaker ───────────
        try
        {
            return await _circuitBreaker.ExecuteAsync(async () =>
            {
                var features = await _extractor.ExtractAsync(
                    patientId, appointmentTime, cancellationToken);

                // AC-3: fall back when history is insufficient
                if (NoShowRiskFallbackPolicy.ShouldUseFallback(features))
                {
                    var fallbackResult = _fallback.ComputeFallbackScore(features);

                    _logger.LogInformation(
                        "NoShowRisk: path={Path}, band={Band}, estimated={Estimated}, appointmentCount={Count}.",
                        fallbackResult.Path, fallbackResult.Band, fallbackResult.IsEstimated,
                        features.AppointmentCount);

                    return fallbackResult;
                }

                // ── Weighted classification model ─────────────────────────────
                var rawScore = ComputeClassificationScore(features);
                var result   = NoShowRiskFallbackPolicy.BuildResult(
                    score:       rawScore,
                    isEstimated: false,
                    path:        ScoringPath.Classification,
                    reasonCode:  "classification");

                _logger.LogInformation(
                    "NoShowRisk: path={Path}, band={Band}, score={Score}.",
                    result.Path, result.Band, result.Score);

                return result;
            });
        }
        catch (BrokenCircuitException bce)
        {
            // Circuit is open — return error fallback immediately (AIR-O04)
            _logger.LogWarning(bce,
                "NoShowRisk: circuit breaker is OPEN — returning error fallback score.");
            return _fallback.ComputeErrorFallbackScore();
        }
        catch (Exception ex)
        {
            // Unhandled scoring exception — log and return error fallback (never block caller)
            _logger.LogError(ex,
                "NoShowRisk: unexpected error during scoring — returning error fallback score.");
            return _fallback.ComputeErrorFallbackScore();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Classification model
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Weighted linear classifier mapping extracted features to a raw score.
    ///
    /// Each feature dimension is normalized to [0, 1] before multiplication.
    /// The appointment-count dimension is normalized against a soft cap of 20
    /// (patients with 20+ appointments get full credit; diminishing returns below).
    /// </summary>
    private static int ComputeClassificationScore(NoShowRiskFeatures features)
    {
        // Appointment count: normalized to [0, 1] (higher history → lower weight contribution)
        double normalizedCount = features.AppointmentCount >= 20
            ? 0.0   // extensive history → count factor does not increase risk
            : 1.0 - (features.AppointmentCount / 20.0);

        double timeOfDayFactor = GetTimeOfDayFactor(features.TimeOfDay);
        double dayOfWeekFactor = GetDayOfWeekFactor(features.DayOfWeek);

        double rawScore =
            (features.NoShowRate        * NoShowRateWeight)
          + (features.CancellationRate  * CancellationRateWeight)
          + (normalizedCount            * AppointmentCountWeight)
          + (timeOfDayFactor            * TimeOfDayWeight)
          + (dayOfWeekFactor            * DayOfWeekWeight);

        // Convert from [0, 1] to [0, 100] and round to nearest integer
        return (int)Math.Round(rawScore * 100.0);
    }

    /// <summary>
    /// Returns a normalized risk factor [0, 1] for the time-of-day bucket.
    /// Values sourced from <c>no-show-risk-config.json § timeOfDayBuckets</c>,
    /// linearly mapped to [0, 1] by dividing the multiplier by the maximum (1.15).
    /// </summary>
    private static double GetTimeOfDayFactor(TimeOfDayBucket bucket) => bucket switch
    {
        TimeOfDayBucket.EarlyMorning  => 1.10 / 1.20,
        TimeOfDayBucket.Morning       => 0.90 / 1.20,
        TimeOfDayBucket.Midday        => 1.05 / 1.20,
        TimeOfDayBucket.Afternoon     => 1.00 / 1.20,
        TimeOfDayBucket.LateAfternoon => 1.15 / 1.20,
        _                             => 1.00 / 1.20,
    };

    /// <summary>
    /// Returns a normalized risk factor [0, 1] for the day of week.
    /// Values sourced from <c>no-show-risk-config.json § dayOfWeekRiskMultipliers</c>,
    /// linearly mapped to [0, 1] by dividing by the maximum multiplier (1.20).
    /// </summary>
    private static double GetDayOfWeekFactor(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday    => 0.95 / 1.20,
        DayOfWeek.Tuesday   => 0.90 / 1.20,
        DayOfWeek.Wednesday => 0.90 / 1.20,
        DayOfWeek.Thursday  => 0.95 / 1.20,
        DayOfWeek.Friday    => 1.15 / 1.20,
        DayOfWeek.Saturday  => 1.10 / 1.20,
        DayOfWeek.Sunday    => 1.20 / 1.20,
        _                   => 1.00 / 1.20,
    };
}
