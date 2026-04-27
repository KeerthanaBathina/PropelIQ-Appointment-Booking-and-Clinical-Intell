namespace UPACIP.Service.Documents;

/// <summary>
/// Abstraction for the Redis FIFO document parsing queue (US_071 TASK_003, AC-3).
///
/// <para>
/// Provides the operations needed to enqueue, dequeue, and inspect the Redis list that
/// backs the <see cref="DocumentParsingDispatcher"/> processing loop.  A separate
/// abstraction exists from <see cref="IDocumentParsingQueueService"/> (which orchestrates
/// the full document lifecycle) to allow the queue monitor to perform read-only health
/// checks without depending on EF Core.
/// </para>
///
/// <para>
/// Implementations must be resilient to transient Redis failures — all methods soft-fail
/// (return zero / null / empty) when the Redis connection is temporarily unavailable
/// so monitoring does not interrupt the API's normal request handling (AC-4).
/// </para>
/// </summary>
public interface IDocumentParsingQueue
{
    /// <summary>
    /// Pushes <paramref name="job"/> onto the tail of the FIFO queue (RPUSH),
    /// maintaining insertion-order processing (AC-3).
    /// </summary>
    Task EnqueueAsync(DocumentParsingQueueJob job, CancellationToken ct = default);

    /// <summary>
    /// Dequeues the oldest item from the head of the queue (LPOP).
    /// Returns <see langword="null"/> when the queue is empty.
    /// </summary>
    Task<DocumentParsingQueueJob?> DequeueAsync(CancellationToken ct = default);

    /// <summary>
    /// Re-enqueues <paramref name="job"/> at the front of the queue (LPUSH) so it is
    /// processed next — used for restart recovery of in-flight items (AC-3, edge case).
    /// </summary>
    Task ReenqueueFrontAsync(DocumentParsingQueueJob job, CancellationToken ct = default);

    /// <summary>
    /// Returns the current number of items waiting in the queue (LLEN).
    /// Returns 0 when the queue is empty or Redis is unavailable.
    /// </summary>
    Task<long> GetDepthAsync(CancellationToken ct = default);

    /// <summary>
    /// Peeks at the oldest item in the queue (LINDEX 0) and returns its age.
    /// Returns <see langword="null"/> when the queue is empty or Redis is unavailable.
    /// </summary>
    Task<TimeSpan?> GetOldestItemAgeAsync(CancellationToken ct = default);
}
