namespace UPACIP.Service.Notifications;

/// <summary>
/// Coordinates a single checkpoint-aware reminder batch run for either the 24-hour
/// or 2-hour reminder window (US_035).
///
/// Responsibilities:
/// <list type="number">
///   <item>Query appointments eligible for the given window using UTC bounds.</item>
///   <item>Skip appointments that are cancelled or already have a successful reminder
///     log for this notification type.</item>
///   <item>Resume from the last Redis-persisted checkpoint when the previous run was
///     interrupted mid-batch (EC-1).</item>
///   <item>Delegate per-appointment dispatch to <see cref="IReminderNotificationService"/>
///     without embedding channel logic here.</item>
///   <item>Persist a checkpoint in Redis after each successful appointment so a
///     restart can continue from the next record (EC-1).</item>
///   <item>Emit structured metrics (processed, skipped, failed, duration) so
///     operations teams can verify the AC-4 10-minute batch budget.</item>
/// </list>
///
/// This interface never throws.  All outcomes are encoded in
/// <see cref="ReminderBatchResult"/>.
/// </summary>
public interface IReminderBatchSchedulerService
{
    /// <summary>
    /// Executes the reminder batch described by <paramref name="context"/>.
    /// </summary>
    /// <param name="context">
    /// Batch type, UTC appointment window, clinic timezone, and run ID.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token passed by the host shutdown or an explicit caller.
    /// When cancelled mid-batch the method returns a partial result with
    /// <see cref="ReminderBatchResult.CompletedFullBatch"/> = <c>false</c>.
    /// </param>
    /// <returns>
    /// A <see cref="ReminderBatchResult"/> summarising processed, skipped, and failed
    /// counts together with the final checkpoint cursor.
    /// </returns>
    Task<ReminderBatchResult> RunBatchAsync(
        ReminderBatchExecutionContext context,
        CancellationToken cancellationToken = default);
}
