namespace UPACIP.Service.Performance.Models;

/// <summary>
/// Per-priority concurrency limits for <see cref="IPriorityRequestQueue"/>
/// (US_081 task_002, edge case — priority queuing under peak load).
///
/// Bind from the <c>"PriorityQueue"</c> configuration section:
/// <code>
/// "PriorityQueue": {
///   "CriticalConcurrency":    20,
///   "NormalConcurrency":      10,
///   "BackgroundConcurrency":   5
/// }
/// </code>
///
/// <list type="bullet">
///   <item><b>Critical</b> (booking) — 20 concurrent slots; booking requests bypass the queue
///     entirely but the semaphore is still available for future Critical workloads.</item>
///   <item><b>Normal</b> (medical coding) — 10 concurrent AI inference slots.</item>
///   <item><b>Background</b> (document parsing) — 5 concurrent; excess requests are held in
///     the Redis queue at the dispatcher level (never rejected).</item>
/// </list>
/// </summary>
public sealed class PriorityQueueOptions
{
    public const string SectionName = "PriorityQueue";

    /// <summary>
    /// Maximum number of simultaneous in-flight Critical-priority requests.
    /// Default: 20.
    /// </summary>
    public int CriticalConcurrency { get; set; } = 20;

    /// <summary>
    /// Maximum number of simultaneous in-flight Normal-priority requests.
    /// Default: 10.
    /// </summary>
    public int NormalConcurrency { get; set; } = 10;

    /// <summary>
    /// Maximum number of simultaneous in-flight Background-priority requests.
    /// Default: 5.
    /// </summary>
    public int BackgroundConcurrency { get; set; } = 5;

    /// <summary>
    /// Maximum number of pending work items held in each in-process channel before back-pressure
    /// is applied (i.e., <c>EnqueueAsync</c> blocks until a slot opens).
    /// Default: 100 per channel.
    /// </summary>
    public int ChannelCapacity { get; set; } = 100;
}
