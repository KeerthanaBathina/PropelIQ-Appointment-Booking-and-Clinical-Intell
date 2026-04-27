namespace UPACIP.Service.Documents;

/// <summary>
/// Monitors the health of the Redis document parsing queue by checking queue depth
/// and stale item age (US_071 TASK_003, AC-4).
///
/// <para>
/// The monitor is invoked periodically by <see cref="QueueMonitorJob"/>.  It emits:
/// <list type="bullet">
///   <item>
///     <see cref="Microsoft.Extensions.Logging.LogLevel.Warning"/> when the oldest
///     queued item has been waiting longer than the configured stale threshold
///     (default 5 minutes).
///   </item>
///   <item>
///     <see cref="Microsoft.Extensions.Logging.LogLevel.Critical"/> when queue depth
///     exceeds the escalation threshold (default 50 items), alerting admins via
///     Serilog/Seq without requiring an external notification system.
///   </item>
/// </list>
/// </para>
/// </summary>
public interface IQueueMonitorService
{
    /// <summary>
    /// Reads queue depth and oldest-item age from Redis, logs structured metrics, and
    /// emits warning / critical events when thresholds are breached (US_071 AC-4).
    /// </summary>
    Task CheckQueueHealthAsync(CancellationToken ct = default);
}
