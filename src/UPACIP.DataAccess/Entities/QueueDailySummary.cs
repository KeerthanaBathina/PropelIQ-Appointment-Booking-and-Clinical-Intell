namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Pre-aggregated daily queue metrics snapshot used to serve fast history
/// and CSV-export queries without runtime GROUP BY scans on the full QueueEntry
/// table (US_056 AC-3, AC-4, NFR-004).
///
/// Rows are populated nightly (or on-demand) by the queue history aggregation
/// job.  A unique constraint on (<see cref="SummaryDate"/>, <see cref="ProviderId"/>,
/// <see cref="AppointmentType"/>) allows the job to safely upsert each row.
///
/// A <c>null</c> <see cref="ProviderId"/> represents the all-provider roll-up row
/// for that date. A <c>null</c> <see cref="AppointmentType"/> represents the
/// all-type roll-up row.
/// </summary>
public sealed class QueueDailySummary : BaseEntity
{
    /// <summary>
    /// Calendar day (UTC) that the summary covers.
    /// Stored as a PostgreSQL <c>date</c> column (no time component).
    /// </summary>
    public DateOnly SummaryDate { get; set; }

    /// <summary>
    /// FK to <c>asp_net_users.Id</c> (provider user) — null for all-provider roll-ups.
    /// ON DELETE SET NULL so summary rows survive provider account deletion.
    /// </summary>
    public Guid? ProviderId { get; set; }

    /// <summary>
    /// Appointment type label (e.g. "Checkup", "Follow-up") — null for all-type roll-ups.
    /// Max 50 characters, matching <see cref="Appointment.AppointmentType"/>.
    /// </summary>
    public string? AppointmentType { get; set; }

    /// <summary>
    /// Mean wait time in fractional minutes across all queue entries for this day/provider/type.
    /// Null when no entries exist for the partition.
    /// </summary>
    public decimal? AvgWaitTimeMinutes { get; set; }

    /// <summary>Count of queue entries with <c>Status = NoShow</c> for this partition.</summary>
    public int NoShowCount { get; set; }

    /// <summary>Count of queue entries with <c>Status = Completed</c> for this partition (patient throughput).</summary>
    public int CompletedCount { get; set; }

    /// <summary>Total number of queue entries for this partition (all statuses).</summary>
    public int TotalPatients { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>Provider user navigation — null for all-provider roll-up rows.</summary>
    public ApplicationUser? Provider { get; set; }
}
