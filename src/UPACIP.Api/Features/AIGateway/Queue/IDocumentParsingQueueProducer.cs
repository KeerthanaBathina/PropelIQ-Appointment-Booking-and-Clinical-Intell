using UPACIP.Api.Features.AIGateway.Contracts;

namespace UPACIP.Api.Features.AIGateway.Queue;

/// <summary>
/// Enqueues document parsing AI requests onto the Redis-backed FIFO queue
/// (US_067 TASK_003, AC-4).
///
/// The caller receives a <see cref="QueueJobReceipt"/> immediately so it can
/// return HTTP 202 Accepted while the job is processed asynchronously in the
/// background by <see cref="DocumentParsingQueueConsumer"/>.
/// </summary>
public interface IDocumentParsingQueueProducer
{
    /// <summary>
    /// Serializes <paramref name="request"/> into a <see cref="QueueMessage"/> and pushes
    /// it onto the Redis FIFO queue using <c>RPUSH</c>.
    /// </summary>
    /// <param name="request">The AI Gateway request to enqueue.</param>
    /// <param name="documentId">
    /// The <c>ClinicalDocument</c> entity identifier; stored on the message for
    /// downstream tracing and audit logging.
    /// </param>
    /// <param name="correlationId">Originating HTTP request correlation ID.</param>
    /// <param name="cancellationToken">Propagated from the HTTP request lifetime.</param>
    /// <returns>
    /// A <see cref="QueueJobReceipt"/> containing the assigned <c>JobId</c> and current
    /// queue depth so callers can surface queue saturation warnings if needed.
    /// </returns>
    Task<QueueJobReceipt> EnqueueAsync(
        AIRequest         request,
        Guid              documentId,
        string            correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the current depth of the active processing queue.</summary>
    Task<long> GetQueueDepthAsync(CancellationToken cancellationToken = default);
}

/// <summary>Receipt returned to the caller after a successful enqueue operation.</summary>
/// <param name="JobId">Unique identifier assigned to the queued job.</param>
/// <param name="QueueDepth">Queue depth immediately after the push (approximate).</param>
public sealed record QueueJobReceipt(Guid JobId, long QueueDepth);
