using System.ComponentModel.DataAnnotations;

namespace UPACIP.Api.Features.AIGateway.Configuration;

/// <summary>
/// Strongly-typed configuration POCO for the AI Gateway Redis request queue
/// (US_067 TASK_003, AC-4, TR-012, NFR-029).
///
/// Bound from the <c>"AIGateway:Queue"</c> configuration section.
/// Controls the Redis list keys, consumer concurrency ceiling, polling cadence,
/// queue depth limits, and retry behaviour for the document parsing queue.
/// </summary>
public sealed class QueueOptions
{
    public const string SectionName = "AIGateway:Queue";

    /// <summary>Redis LIST key used as the FIFO processing queue.</summary>
    [Required]
    public string QueueKey { get; set; } = "ai-gateway:document-parsing:queue";

    /// <summary>Redis LIST key for permanently failed jobs (dead-letter queue).</summary>
    [Required]
    public string DeadLetterKey { get; set; } = "ai-gateway:document-parsing:dead-letter";

    /// <summary>
    /// Maximum number of jobs processed concurrently by the background consumer.
    /// Bounded by <see cref="SemaphoreSlim"/> in the consumer worker (AC-4).
    /// </summary>
    [Range(1, 50)]
    public int MaxConcurrentWorkers { get; set; } = 3;

    /// <summary>
    /// Soft limit on queue depth. A structured warning is logged when depth exceeds
    /// 80 % of this value. Queue accepts messages beyond this limit.
    /// </summary>
    [Range(1, 10_000)]
    public int MaxQueueDepth { get; set; } = 100;

    /// <summary>Consumer polling interval in milliseconds. Defaults to 500 ms.</summary>
    [Range(100, 60_000)]
    public int PollingIntervalMs { get; set; } = 500;

    /// <summary>
    /// Maximum retry attempts before a failed job is moved to the dead-letter queue.
    /// </summary>
    [Range(0, 10)]
    public int MaxRetries { get; set; } = 3;

    /// <summary>Processing priority applied when none is specified. Defaults to Normal.</summary>
    public QueueJobPriority DefaultPriority { get; set; } = QueueJobPriority.Normal;

    /// <summary>
    /// Grace period in seconds for in-flight jobs to complete during graceful shutdown.
    /// </summary>
    [Range(1, 120)]
    public int ShutdownDrainTimeoutSeconds { get; set; } = 30;

    /// <summary>Interval in seconds for queue depth monitoring logs.</summary>
    [Range(5, 3_600)]
    public int MonitoringIntervalSeconds { get; set; } = 30;
}

/// <summary>Processing priority for queued AI Gateway jobs.</summary>
public enum QueueJobPriority
{
    Normal = 0,
    Urgent = 1,
}
