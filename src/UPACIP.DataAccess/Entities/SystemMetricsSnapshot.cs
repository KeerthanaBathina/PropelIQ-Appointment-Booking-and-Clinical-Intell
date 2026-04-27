namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Pre-aggregated daily system metrics snapshot for the Admin Dashboard trend charts
/// (US_058 AC-1, AC-2, NFR-004).
///
/// <para>
/// One row is upserted per UTC calendar day by the <c>SystemMetricsService</c> or a scheduled
/// background aggregation job.  The <see cref="MetricDate"/> unique constraint guarantees
/// idempotent upserts without creating duplicate rows for the same day.
/// </para>
///
/// <para>
/// The index on <see cref="MetricDate"/> enables efficient date-range scans for rolling
/// 7-day and 30-day trend queries (AC-2).  Storing decimal rate values as
/// <c>numeric(5,2)</c> preserves one-decimal-place precision across the [0.00–100.00] range.
/// </para>
///
/// <para>
/// Does NOT inherit <see cref="BaseEntity"/> (matches <see cref="AgreementRateMetric"/> pattern):
/// it uses a dedicated <c>SnapshotId</c> PK and the <c>UpdatedAt</c> field tracks the most
/// recent recalculation timestamp for the same calendar day.
/// </para>
/// </summary>
public sealed class SystemMetricsSnapshot
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid SnapshotId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// UTC calendar date this snapshot covers.
    /// A unique constraint allows safe daily upserts (<c>ON CONFLICT DO UPDATE</c>).
    /// </summary>
    public DateOnly MetricDate { get; set; }

    /// <summary>Count of distinct users who logged in during this calendar day.</summary>
    public int ActiveUsers { get; set; }

    /// <summary>Count of non-cancelled appointments scheduled on this calendar day.</summary>
    public int DailyAppointments { get; set; }

    /// <summary>
    /// No-show rate for this calendar day expressed as a percentage [0.00–100.00].
    /// Stored as <c>numeric(5,2)</c>.
    /// </summary>
    public decimal NoShowRate { get; set; }

    /// <summary>
    /// AI code-approval agreement rate expressed as a percentage [0.00–100.00].
    /// Stored as <c>numeric(5,2)</c>.
    /// </summary>
    public decimal AiAgreementRate { get; set; }

    /// <summary>
    /// Platform uptime percentage for this day [0.00–100.00].
    /// Derived from health-check aggregation results.
    /// Stored as <c>numeric(5,2)</c>.
    /// </summary>
    public decimal UptimePercent { get; set; }

    /// <summary>UTC timestamp when this snapshot row was first created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the most recent recalculation for this date.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
