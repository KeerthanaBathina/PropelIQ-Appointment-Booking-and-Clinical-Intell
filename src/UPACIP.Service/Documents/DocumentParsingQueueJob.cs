using System.Text.Json.Serialization;

namespace UPACIP.Service.Documents;

/// <summary>
/// Immutable payload stored in the Redis parsing queue (US_039 AC-1, AC-4).
///
/// Each entry represents one queued attempt to parse a <see cref="DocumentId"/>.
/// The <see cref="AttemptNumber"/> starts at 1 and is incremented when the job is
/// re-enqueued for a retry attempt via the back-off path. Within a single dispatch the
/// Polly resilience pipeline handles transient in-process retries, so this counter
/// represents queue-level (re-enqueue) attempts only.
///
/// Serialized to JSON for Redis storage via <c>System.Text.Json</c>.
/// </summary>
public sealed record DocumentParsingQueueJob
{
    /// <summary>Primary key of the <c>clinical_documents</c> row to parse.</summary>
    [JsonPropertyName("documentId")]
    public Guid DocumentId { get; init; }

    /// <summary>
    /// Queue-level attempt counter (1-based). Used for structured log correlation
    /// and future re-enqueue scenarios.
    /// </summary>
    [JsonPropertyName("attemptNumber")]
    public int AttemptNumber { get; init; } = 1;

    /// <summary>
    /// UTC timestamp at which this job was enqueued. Used for FIFO ordering
    /// diagnostics and queue-age alerting.
    /// </summary>
    [JsonPropertyName("enqueuedAt")]
    public DateTimeOffset EnqueuedAt { get; init; } = DateTimeOffset.UtcNow;
}
