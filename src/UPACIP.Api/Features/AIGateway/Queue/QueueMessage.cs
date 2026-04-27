using UPACIP.Api.Features.AIGateway.Configuration;
using UPACIP.Api.Features.AIGateway.Contracts;

namespace UPACIP.Api.Features.AIGateway.Queue;

/// <summary>
/// Queue message schema for document parsing jobs flowing through the AI Gateway
/// Redis queue (US_067 TASK_003, AC-4).
///
/// Messages are serialized to JSON using camelCase naming policy and pushed onto
/// a Redis LIST with <c>RPUSH</c> (producer) / <c>LPOP</c> (consumer) — FIFO order.
/// The schema is versioned via <see cref="SchemaVersion"/> to support forward-compatible
/// schema evolution without queue drain downtime.
/// </summary>
public sealed class QueueMessage
{
    /// <summary>Schema version for forward-compatible deserialization.</summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>Unique job identifier assigned at enqueue time; used for status tracking.</summary>
    public Guid JobId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Reference to the <c>ClinicalDocument</c> entity being processed.
    /// Passed through to the <see cref="AIRequest.Metadata"/> bag for audit logging.
    /// </summary>
    public Guid DocumentId { get; init; }

    /// <summary>The full AI Gateway request payload to be processed by the consumer.</summary>
    public AIRequest Request { get; init; } = null!;

    /// <summary>Processing priority — consumers may use this to re-order processing.</summary>
    public QueueJobPriority Priority { get; init; } = QueueJobPriority.Normal;

    /// <summary>UTC timestamp captured when the message was enqueued.</summary>
    public DateTimeOffset EnqueuedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Current retry attempt number (0-based).
    /// Incremented by the consumer before re-enqueue on transient failure.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>Maximum allowed retries before routing to the dead-letter queue.</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>Correlation ID from the originating HTTP request for distributed tracing.</summary>
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>
    /// Whether the job has exceeded its retry budget and belongs in the dead-letter queue.
    /// </summary>
    public bool IsExhausted => RetryCount >= MaxRetries;
}
