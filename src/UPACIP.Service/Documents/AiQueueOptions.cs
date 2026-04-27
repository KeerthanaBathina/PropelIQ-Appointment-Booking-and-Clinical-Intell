namespace UPACIP.Service.Documents;

/// <summary>
/// Configuration options for the AI document parsing queue monitor
/// (US_071 TASK_003, AC-3, AC-4).
///
/// Bound from the <c>AiQueue</c> configuration section in appsettings.json.
/// </summary>
public sealed class AiQueueOptions
{
    /// <summary>Configuration section key in appsettings.json.</summary>
    public const string SectionName = "AiQueue";

    /// <summary>
    /// How often (seconds) the queue monitor job polls for health metrics.
    /// Default: 60 s — balanced between observability latency and Redis LLEN call volume.
    /// </summary>
    public int MonitorIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Age threshold (minutes) after which a queued item is considered stale and a
    /// <c>LogWarning</c> is emitted (US_071 AC-4).  Default: 5 minutes.
    /// </summary>
    public int StaleWarningMinutes { get; set; } = 5;

    /// <summary>
    /// Queue depth threshold above which an admin escalation <c>LogCritical</c> is
    /// emitted (US_071 AC-4).  Default: 50 items.
    /// </summary>
    public int EscalationDepthThreshold { get; set; } = 50;
}
