namespace UPACIP.Service.Audit;

/// <summary>
/// Redis-backed failover queue for audit log entries (US_064 edge case: DB unavailable).
///
/// When <see cref="UPACIP.Service.Auth.IAuditLogService"/> fails to write to PostgreSQL,
/// it delegates to this service to enqueue the entry in Redis so that eventual persistence
/// is guaranteed once DB connectivity restores.
///
/// Implementations must be thread-safe (Singleton lifetime in DI).
/// </summary>
public interface IAuditQueueService
{
    /// <summary>
    /// Serializes <paramref name="entry"/> and appends it to the Redis failover queue using
    /// <c>RPUSH</c>.  When Redis is also unavailable, falls back to a local JSON file.
    /// Never throws — failures are logged but the calling operation is unaffected (fail-open).
    /// </summary>
    Task EnqueueAsync(AuditLogQueueEntry entry, CancellationToken ct = default);

    /// <summary>
    /// Removes and returns the oldest entry from the queue (<c>LPOP</c>).
    /// Returns <c>null</c> when the queue is empty or Redis is unavailable.
    /// </summary>
    Task<AuditLogQueueEntry?> DequeueAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the current queue depth (<c>LLEN</c>).
    /// Returns <c>-1</c> if Redis is unavailable.
    /// </summary>
    Task<long> GetQueueDepthAsync(CancellationToken ct = default);
}
