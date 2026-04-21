using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.AI.NoShowRisk;

/// <summary>
/// Risk band derived from the raw 0-100 score (AC-2 thresholds from scoring config).
/// Shared with <c>UPACIP.DataAccess.Enums.NoShowRiskBand</c> — same enum used for entity
/// persistence and service output so no mapping is needed between layers.
/// </summary>
/// <seealso cref="UPACIP.DataAccess.Enums.NoShowRiskBand"/>

/// <summary>
/// Output contract returned by <see cref="INoShowRiskScoringService"/> for a single
/// appointment risk evaluation (AIR-006, FR-014, US_026).
///
/// Downstream consumers (slot-swap prioritisation, staff API) read only this model;
/// they never depend on internal feature values so scoring internals can change without
/// breaking callers (AC-4).
/// </summary>
public sealed record NoShowRiskScoreResult
{
    /// <summary>Normalized risk score clamped to [0, 100] (EC-2).</summary>
    public int Score { get; init; }

    /// <summary>Discrete risk band for color coding and priority routing (AC-2).</summary>
    public NoShowRiskBand Band { get; init; }

    /// <summary>
    /// True when the score was produced by rule-based fallback because the patient
    /// has fewer than the configured minimum history appointments (AC-3).
    /// </summary>
    public bool IsEstimated { get; init; }

    /// <summary>
    /// True when the score exceeds the high-risk outreach threshold configured in
    /// <c>no-show-risk-config.json</c> (EC-2).  Signals staff workflows to initiate
    /// proactive outreach before the appointment.
    /// </summary>
    public bool RequiresOutreach { get; init; }

    /// <summary>
    /// Indicates which scoring path was used for structured audit logging
    /// (AIR-S01, NFR-017).  Never contains raw patient history values.
    /// </summary>
    public ScoringPath Path { get; init; }

    /// <summary>Short reason code for audit trail (e.g. "classification", "insufficient_history").</summary>
    public string ReasonCode { get; init; } = string.Empty;
}

/// <summary>
/// Identifies which code path produced the score so audit logs can distinguish
/// classification model runs from deterministic fallback runs (AIR-S04, NFR-017).
/// </summary>
public enum ScoringPath
{
    /// <summary>Score produced by the weighted classification model.</summary>
    Classification,

    /// <summary>Score produced by rule-based fallback (insufficient history).</summary>
    RuleBasedFallback,

    /// <summary>Score produced by fallback because the model path encountered an error.</summary>
    ErrorFallback,
}
