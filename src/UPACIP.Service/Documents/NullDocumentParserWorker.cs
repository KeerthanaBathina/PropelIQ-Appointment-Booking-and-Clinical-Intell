using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Documents;

/// <summary>
/// Placeholder <see cref="IDocumentParserWorker"/> implementation used until
/// task_003_ai_document_parser_worker_and_retry_orchestration provides the
/// real AI parsing logic.
///
/// This stub transitions the document status to <c>Completed</c> so the dispatcher
/// and queue infrastructure can be verified end-to-end without the AI layer.
/// Replace this registration in <c>Program.cs</c> with the task_003 implementation.
/// </summary>
public sealed class NullDocumentParserWorker : IDocumentParserWorker
{
    private readonly ApplicationDbContext                   _db;
    private readonly ILogger<NullDocumentParserWorker>     _logger;

    public NullDocumentParserWorker(
        ApplicationDbContext                db,
        ILogger<NullDocumentParserWorker>   logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task ParseAsync(Guid documentId, CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "NullDocumentParserWorker invoked for DocumentId={DocumentId}. " +
            "AI parsing is not yet implemented — transitioning to Completed as a stub. " +
            "Replace this registration with the task_003 implementation.",
            documentId);

        var document = await _db.ClinicalDocuments.FindAsync(
            new object[] { documentId }, cancellationToken);

        if (document is null)
        {
            _logger.LogError(
                "NullDocumentParserWorker: document not found. DocumentId={DocumentId}", documentId);
            return;
        }

        document.ProcessingStatus = ProcessingStatus.Completed;
        document.UpdatedAt        = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }
}
