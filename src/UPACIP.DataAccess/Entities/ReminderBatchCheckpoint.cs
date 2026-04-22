using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Persists the checkpoint cursor for a single 24-hour or 2-hour reminder batch run
/// so the worker can resume from the last successfully processed appointment after a
/// mid-batch failure or host restart (US_035 EC-1, EC-2).
///
/// <para><b>Keying (EC-2):</b> Each row is uniquely identified by
/// (<see cref="BatchType"/>, <see cref="WindowDateUtc"/>) so the two reminder windows
/// always maintain independent progress and neither can overwrite the other.</para>
///
/// <para><b>Resume cursor:</b> <see cref="LastProcessedAppointmentId"/> and
/// <see cref="LastProcessedAppointmentTimeUtc"/> together give the worker enough
/// context to resume the ordered (AppointmentTime, Id) scan without replaying
/// appointments that already succeeded.</para>
///
/// <para><b>Lifetime:</b> Rows are upserted at the start of each batch run and
/// marked <see cref="ReminderBatchRunStatus.Completed"/> when the full window is
/// processed.  Completed rows are kept for 7 days for operational audit queries
/// and then pruned.</para>
/// </summary>
public sealed class ReminderBatchCheckpoint
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Whether this checkpoint belongs to the 24-hour or 2-hour reminder window.
    /// Stored as the integer value of the enum for compact storage and fast index
    /// range scans.
    /// </summary>
    public int BatchType { get; set; }

    /// <summary>
    /// UTC calendar date identifying the window this batch covered.
    /// For <c>TwentyFourHour</c> this is the date on which the batch ran;
    /// for <c>TwoHour</c> this is the UTC date portion of <c>WindowStartUtc</c>.
    /// Used together with <see cref="BatchType"/> as the logical unique key (EC-2).
    /// </summary>
    public DateTime WindowDateUtc { get; set; }

    /// <summary>UTC start of the appointment window (inclusive).</summary>
    public DateTime WindowStartUtc { get; set; }

    /// <summary>UTC end of the appointment window (exclusive).</summary>
    public DateTime WindowEndUtc { get; set; }

    /// <summary>
    /// <c>Id</c> of the last appointment that was successfully dispatched in this run.
    /// <c>null</c> when the batch started but has not yet processed any appointment.
    /// The worker uses this together with <see cref="LastProcessedAppointmentTimeUtc"/>
    /// to find the resume position in the ordered (AppointmentTime, Id) result set.
    /// </summary>
    public Guid? LastProcessedAppointmentId { get; set; }

    /// <summary>
    /// <see cref="Appointment.AppointmentTime"/> of the last processed appointment.
    /// Stored alongside the ID to allow the resume query to seek efficiently on the
    /// composite <c>(appointment_time, id)</c> index without a full table scan.
    /// </summary>
    public DateTime? LastProcessedAppointmentTimeUtc { get; set; }

    /// <summary>Number of appointments successfully processed in this run so far.</summary>
    public int ProcessedCount { get; set; }

    /// <summary>Number of appointments skipped (already reminded or resumed-over).</summary>
    public int SkippedCount { get; set; }

    /// <summary>Number of appointments where dispatch returned a failure result.</summary>
    public int FailedCount { get; set; }

    /// <summary>Current run status.</summary>
    public ReminderBatchRunStatus RunStatus { get; set; } = ReminderBatchRunStatus.Running;

    /// <summary>
    /// Unique run identifier set by the worker for each execution.  Useful for
    /// correlating batch log entries to a specific checkpoint row.
    /// </summary>
    public Guid RunId { get; set; }

    /// <summary>UTC timestamp when this checkpoint row was first created (batch start).</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the most recent upsert (updated after each appointment).</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
