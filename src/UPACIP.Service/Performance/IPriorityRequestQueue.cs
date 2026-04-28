using UPACIP.Service.Performance.Models;

namespace UPACIP.Service.Performance;

/// <summary>
/// Throttles concurrent AI requests by category via a multi-priority bounded channel
/// (US_081 task_002, edge case — priority queuing under peak load).
///
/// <para>
/// <b>Priority levels:</b>
/// <list type="table">
///   <item><c>Critical</c> — not queued; callers invoke work immediately with concurrency guard.</item>
///   <item><c>Normal</c>   — medical coding; up to <c>NormalConcurrency</c> parallel calls.</item>
///   <item><c>Background</c> — document parsing; up to <c>BackgroundConcurrency</c> parallel calls.</item>
/// </list>
/// </para>
///
/// <para>
/// <b>Back-pressure:</b> When a priority channel is full (bounded capacity reached),
/// <see cref="ExecuteAsync{T}"/> waits until a slot opens — it never silently drops requests.
/// For Background (parsing), this creates back-pressure to the Redis queue dispatcher level,
/// ensuring parsing requests are delayed rather than lost under peak load.
/// </para>
/// </summary>
public interface IPriorityRequestQueue
{
    /// <summary>
    /// Executes <paramref name="work"/> subject to the concurrency limit for
    /// <paramref name="priority"/>.
    /// </summary>
    /// <typeparam name="T">Return type of the work item.</typeparam>
    /// <param name="work">
    /// Async delegate receiving a combined cancellation token (caller token + queue shutdown).
    /// </param>
    /// <param name="priority">
    /// Target priority tier determining concurrency ceiling and channel.
    /// </param>
    /// <param name="cancellationToken">Caller-provided cancellation token.</param>
    /// <returns>Result produced by <paramref name="work"/>.</returns>
    Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> work,
        RequestPriority                  priority,
        CancellationToken                cancellationToken = default);
}
