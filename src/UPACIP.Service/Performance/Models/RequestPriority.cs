namespace UPACIP.Service.Performance.Models;

/// <summary>
/// Request priority categories for the <see cref="IPriorityRequestQueue"/>
/// (US_081 task_002, edge case — priority queuing under peak load).
/// </summary>
public enum RequestPriority
{
    /// <summary>
    /// Highest priority — time-critical operations that must not be queued.
    /// Currently: appointment booking requests (&lt;2s SLA target).
    /// Booking requests bypass the queue entirely and execute immediately.
    /// </summary>
    Critical = 0,

    /// <summary>
    /// Standard AI inference requests that benefit from concurrency throttling.
    /// Currently: medical coding (5s SLA, max 10 concurrent slots).
    /// </summary>
    Normal = 1,

    /// <summary>
    /// Long-running background operations that can tolerate queuing.
    /// Currently: document parsing (30s SLA, max 5 concurrent slots).
    /// Back-pressure is applied to the Redis queue dispatcher when this channel is full.
    /// </summary>
    Background = 2,
}
