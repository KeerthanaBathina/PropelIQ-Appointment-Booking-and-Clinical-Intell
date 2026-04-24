namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Persisted daily snapshot of the AI-human coding agreement rate (US_050 AC-1, AC-2, FR-067).
/// <para>
/// One row is computed and upserted per calendar day by the agreement-rate calculation job.
/// The <see cref="CalculationDate"/> column carries a unique constraint so the job can safely
/// upsert without creating duplicate rows for the same day.
/// </para>
/// <para>
/// Does NOT inherit <see cref="BaseEntity"/> because it has a dedicated <c>MetricId</c> PK,
/// and <c>UpdatedAt</c> is meaningful (daily recalculation may revise intra-day estimates).
/// </para>
/// </summary>
public sealed class AgreementRateMetric
{
    /// <summary>Surrogate UUID primary key — generated on insert.</summary>
    public Guid MetricId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Calendar date for which this metric row was computed (UTC date, stored as <c>date</c> column).
    /// A unique constraint prevents duplicate rows per day.
    /// </summary>
    public DateOnly CalculationDate { get; set; }

    /// <summary>
    /// Percentage of AI-suggested codes approved without override on this day.
    /// Stored as a value in [0.0, 1.0] (e.g. 0.87 = 87 %).
    /// Precision 5, scale 4 — sufficient for 100.0000 % representation.
    /// </summary>
    public decimal DailyAgreementRate { get; set; }

    /// <summary>
    /// Rolling 30-day agreement rate centred on <see cref="CalculationDate"/>.
    /// <c>null</c> when fewer than 30 days of history are available or when the period
    /// has fewer than <c>MinimumThreshold</c> verified codes.
    /// Precision 5, scale 4.
    /// </summary>
    public decimal? Rolling30DayRate { get; set; }

    /// <summary>Total number of AI-suggested codes that reached a terminal verification state on this day.</summary>
    public int TotalCodesVerified { get; set; }

    /// <summary>
    /// Count of AI-suggested codes approved by staff without any override (agreement).
    /// </summary>
    public int CodesApprovedWithoutOverride { get; set; }

    /// <summary>Count of codes fully overridden by staff (disagreements).</summary>
    public int CodesOverridden { get; set; }

    /// <summary>
    /// Count of codes partially overridden (e.g. description changed but base code kept).
    /// Partial overrides are treated as disagreements per the edge-case requirement (EC-1).
    /// </summary>
    public int CodesPartiallyOverridden { get; set; }

    /// <summary>
    /// <c>true</c> when <see cref="TotalCodesVerified"/> ≥ 50, indicating the daily metric
    /// has reached the minimum threshold for statistical significance (US_050 EC-1).
    /// When <c>false</c> the UI must display "Not enough data".
    /// </summary>
    public bool MeetsMinimumThreshold { get; set; }

    /// <summary>UTC timestamp when this row was first created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the most recent recalculation for this date.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
