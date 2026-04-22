namespace UPACIP.Service.Documents;

/// <summary>
/// Contract for the AI document parser worker (US_039 task_003_ai_document_parser_worker_and_retry_orchestration).
///
/// The dispatcher (<see cref="DocumentParsingDispatcher"/>) calls this interface for every
/// dequeued job.  The implementation is responsible for:
/// <list type="bullet">
///   <item>Transitioning the document status from <c>Queued</c> → <c>Processing</c>.</item>
///   <item>Running AI extraction and persisting <c>ExtractedData</c> rows.</item>
///   <item>Transitioning the document status to <c>Completed</c> on success.</item>
///   <item>Throwing an exception on failure so the caller's Polly pipeline can retry.</item>
/// </list>
/// </summary>
public interface IDocumentParserWorker
{
    /// <summary>
    /// Parses the clinical document identified by <paramref name="documentId"/>.
    /// Throws on transient failure so the dispatcher's resilience pipeline can retry.
    /// </summary>
    /// <param name="documentId">Primary key of the <c>clinical_documents</c> row.</param>
    /// <param name="cancellationToken">Propagated from the background-service host.</param>
    Task ParseAsync(Guid documentId, CancellationToken cancellationToken);
}
