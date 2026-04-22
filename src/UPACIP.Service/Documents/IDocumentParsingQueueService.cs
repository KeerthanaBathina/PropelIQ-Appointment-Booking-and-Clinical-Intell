namespace UPACIP.Service.Documents;

/// <summary>
/// Orchestrates the transition of uploaded documents into the Redis-backed parsing pipeline
/// (US_039 AC-1, EC-1).
/// </summary>
public interface IDocumentParsingQueueService
{
    /// <summary>
    /// Transitions the document identified by <paramref name="documentId"/> from
    /// <c>Uploaded</c> to <c>Queued</c> status and enqueues a parsing job in Redis.
    ///
    /// If Redis is unavailable the method falls back to triggering synchronous in-process
    /// parsing with a logged warning (US_039 EC-1). The caller's HTTP response is not
    /// blocked in the fallback path.
    /// </summary>
    /// <param name="documentId">Primary key of the <c>clinical_documents</c> row.</param>
    /// <param name="cancellationToken">Caller-provided cancellation token.</param>
    Task EnqueueAsync(Guid documentId, CancellationToken cancellationToken = default);
}
