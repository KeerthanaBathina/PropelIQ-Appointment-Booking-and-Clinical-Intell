namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Lifecycle state of a <see cref="UPACIP.DataAccess.Entities.ReminderBatchCheckpoint"/> row.
/// </summary>
public enum ReminderBatchRunStatus
{
    /// <summary>Batch is currently in progress or was interrupted before completing.</summary>
    Running,

    /// <summary>
    /// All appointments in the window were processed without interruption and within
    /// the 10-minute budget (US_035 AC-4).
    /// </summary>
    Completed,

    /// <summary>
    /// Batch exceeded the time budget or was cancelled mid-run.
    /// The checkpoint cursor can still be used to resume.
    /// </summary>
    Interrupted,
}
