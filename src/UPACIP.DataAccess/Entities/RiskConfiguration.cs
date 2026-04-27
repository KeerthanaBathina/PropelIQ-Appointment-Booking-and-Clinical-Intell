namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Singleton runtime configuration record controlling no-show risk scoring
/// and automated outreach behaviour (US_060 AC-3, AC-4).
///
/// <para>
/// The table is designed to hold exactly one row (seeded by the migration).
/// All writes go through the Admin Configuration UI (SCR-015) and are protected
/// by an optimistic-concurrency token (<see cref="Version"/>) to prevent
/// simultaneous admin saves from silently overwriting each other.
/// </para>
///
/// <para>
/// <see cref="ScoringParameters"/> stores a JSON object of per-factor weight
/// coefficients (e.g. <c>{"priorNoShowsWeight":0.50,"cancellationHistoryWeight":0.30,"appointmentLeadTimeWeight":0.20}</c>).
/// The weights must sum to 1.0; enforcement is at the application layer before
/// persistence.
/// </para>
///
/// <para>
/// When an admin changes <see cref="HighRiskThreshold"/> or
/// <see cref="ScoringParameters"/>, the service sets
/// <see cref="RecalculationPending"/> = <c>true</c> and persists both fields
/// atomically.  A background job reads that flag, recomputes patient risk scores,
/// and resets the flag when complete (US_060 AC-3 edge-case: risk threshold
/// changes on active appointments).
/// </para>
/// </summary>
public sealed class RiskConfiguration
{
    /// <summary>Primary key — always the single seeded row.</summary>
    public Guid RiskConfigId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Score threshold above which a patient is classified as high risk (0–100).
    /// Default: <c>75</c>.
    /// </summary>
    public int HighRiskThreshold { get; set; } = 75;

    /// <summary>
    /// Score threshold above which a patient is classified as medium risk (0–100).
    /// Must be less than <see cref="HighRiskThreshold"/>.
    /// Default: <c>45</c>.
    /// </summary>
    public int MediumRiskThreshold { get; set; } = 45;

    /// <summary>
    /// Minimum number of historical appointments required before the AI model
    /// generates a risk score for a patient.
    /// Default: <c>3</c>.
    /// </summary>
    public int MinAppointmentsForAiScore { get; set; } = 3;

    /// <summary>
    /// When <c>true</c>, the notification pipeline automatically sends outreach
    /// messages to patients whose risk score crosses the high-risk threshold.
    /// Default: <c>true</c>.
    /// </summary>
    public bool AutoOutreach { get; set; } = true;

    /// <summary>
    /// JSONB object of per-factor weight coefficients used by the risk scoring model
    /// (e.g. <c>{"priorNoShowsWeight":0.50,"cancellationHistoryWeight":0.30,"appointmentLeadTimeWeight":0.20}</c>).
    /// Weights must sum to 1.0 (enforced at application layer).
    /// Stored as PostgreSQL <c>jsonb</c>.
    /// </summary>
    public string ScoringParameters { get; set; } = string.Empty;

    /// <summary>
    /// Indicates that a threshold or scoring-parameter change has been saved but
    /// historical appointment risk scores have not yet been recomputed by the
    /// background recalculation job (US_060 AC-3 edge-case).
    /// Default: <c>false</c>.
    /// </summary>
    public bool RecalculationPending { get; set; } = false;

    /// <summary>
    /// UTC timestamp of the last completed risk-score batch recalculation.
    /// Null until the first recalculation job runs.
    /// </summary>
    public DateTime? LastRecalculatedAt { get; set; }

    /// <summary>
    /// Optimistic-concurrency token.  Incremented by the service layer on each save;
    /// EF Core includes it in every UPDATE WHERE clause (prevents lost-update anomalies).
    /// Default: <c>0</c>.
    /// </summary>
    public int Version { get; set; } = 0;

    /// <summary>
    /// Identity of the admin user who last modified this configuration (US_060 AC-4).
    /// Null if unchanged from seed defaults.
    /// </summary>
    public Guid? UpdatedByUserId { get; set; }

    /// <summary>UTC timestamp of the last configuration update.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
