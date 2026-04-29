namespace UPACIP.Service.Logging;

/// <summary>
/// Provides access to the correlation ID for the current async execution context
/// (US_095, AC-1, edge case 2).
///
/// Design notes:
///   - Uses <see cref="AsyncLocal{T}"/> so the value flows automatically across
///     <c>await</c> continuations, <c>Task.Run</c> calls, and into child async
///     tasks spawned from the same logical context.
///   - Scoped lifetime in DI — each HTTP request receives its own instance. The
///     <see cref="AsyncLocal{T}"/> ensures that values set in the request scope
///     are isolated from concurrent requests.
///
/// Background job propagation pattern (edge case 2):
///   When a controller action enqueues a background job, the calling code must
///   capture the correlation ID before queueing:
///   <code>
///   var correlationId = _correlationIdAccessor.CorrelationId;
///   _jobQueue.Enqueue(new JobPayload { CorrelationId = correlationId, ... });
///   </code>
///   In the <c>BackgroundService.ExecuteAsync</c>, before processing each job:
///   <code>
///   _correlationIdAccessor.SetCorrelationId(job.CorrelationId);
///   using (LogContext.PushProperty("CorrelationId", job.CorrelationId))
///   {
///       // All logs inside inherit the originating request's correlation ID.
///   }
///   </code>
/// </summary>
public interface ICorrelationIdAccessor
{
    /// <summary>
    /// The current correlation ID for this async context. Returns a newly generated
    /// ID if one has not been set yet (defensive fallback — should not occur in normal
    /// HTTP request flow after CorrelationIdMiddleware runs).
    /// </summary>
    string CorrelationId { get; }

    /// <summary>
    /// Sets the correlation ID for the current async execution context. Called once
    /// per HTTP request by <c>CorrelationIdMiddleware</c> and once per background job
    /// by the job consumer.
    /// </summary>
    void SetCorrelationId(string correlationId);
}

/// <summary>
/// <see cref="AsyncLocal{T}"/>-backed implementation of <see cref="ICorrelationIdAccessor"/>.
/// </summary>
public sealed class CorrelationIdAccessor : ICorrelationIdAccessor
{
    // AsyncLocal<T> stores a value that flows downward through the async context
    // (Task continuations, child Tasks) but changes do NOT propagate back to the
    // parent context. This is the correct semantic for correlation ID propagation.
    private static readonly AsyncLocal<string?> _current = new();

    /// <inheritdoc/>
    public string CorrelationId =>
        _current.Value ?? Guid.NewGuid().ToString("N"); // "N" = 32-char compact hex, no dashes

    /// <inheritdoc/>
    public void SetCorrelationId(string correlationId) =>
        _current.Value = correlationId;
}
