using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Documents;

/// <summary>
/// Transitions uploaded documents into the Redis-backed parsing pipeline (US_039 AC-1).
///
/// Standard path (Redis available):
///   1. Locate the <c>ClinicalDocument</c> row.
///   2. Guard against duplicate enqueues (idempotent).
///   3. Build a <see cref="DocumentParsingQueueJob"/> and RPUSH to the Redis FIFO list.
///   4. Commit the <c>Queued</c> status transition to the database.
///
/// Degraded path (Redis unavailable — EC-1):
///   When a <see cref="RedisException"/> or <see cref="RedisTimeoutException"/> is caught
///   the method logs a structured warning, skips the Redis write, and schedules the
///   document for in-process (synchronous) parsing via <see cref="IDocumentParserWorker"/>
///   on a background thread so the HTTP upload response is not blocked.
/// </summary>
public sealed class DocumentParsingQueueService : IDocumentParsingQueueService
{
    /// <summary>Redis list key for the FIFO parsing queue.  Must match the dispatcher's constant.</summary>
    internal const string QueueKey = "upacip:parsing-queue";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented        = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ApplicationDbContext                    _db;
    private readonly IConnectionMultiplexer                  _redis;
    private readonly IDocumentParserWorker                   _parserWorker;
    private readonly ILogger<DocumentParsingQueueService>    _logger;

    public DocumentParsingQueueService(
        ApplicationDbContext                    db,
        IConnectionMultiplexer                  redis,
        IDocumentParserWorker                   parserWorker,
        ILogger<DocumentParsingQueueService>    logger)
    {
        _db           = db;
        _redis        = redis;
        _parserWorker = parserWorker;
        _logger       = logger;
    }

    /// <inheritdoc/>
    public async Task EnqueueAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        // ── Step 1: Locate the document ─────────────────────────────────────────────
        var document = await _db.ClinicalDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        if (document is null)
        {
            _logger.LogWarning(
                "DocumentParsingQueueService: document not found, skipping enqueue. DocumentId={DocumentId}",
                documentId);
            return;
        }

        // ── Step 2: Idempotency guard ────────────────────────────────────────────────
        // Already past the Uploaded state — do not re-enqueue (AC-1 post-upload trigger only).
        if (document.ProcessingStatus != ProcessingStatus.Uploaded)
        {
            _logger.LogDebug(
                "DocumentParsingQueueService: document already in status {Status}, skipping enqueue. DocumentId={DocumentId}",
                document.ProcessingStatus, documentId);
            return;
        }

        // ── Step 3: Build the queue payload ─────────────────────────────────────────
        var job = new DocumentParsingQueueJob
        {
            DocumentId    = documentId,
            AttemptNumber = 1,
            EnqueuedAt    = DateTimeOffset.UtcNow,
        };

        var jobJson = JsonSerializer.Serialize(job, JsonOptions);

        // ── Step 4: Attempt Redis enqueue ─────────────────────────────────────────────
        try
        {
            var db = _redis.GetDatabase();
            // RPUSH = add to tail → oldest item is at head → LPOP gives FIFO ordering (EC-2).
            await db.ListRightPushAsync(QueueKey, jobJson);

            // ── Step 5: Commit Queued status (only after Redis write succeeds) ────────
            document.ProcessingStatus = ProcessingStatus.Queued;
            document.UpdatedAt        = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "DocumentParsingQueueService: job enqueued. DocumentId={DocumentId} QueueKey={QueueKey}",
                documentId, QueueKey);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException or RedisConnectionException)
        {
            // ── EC-1: Redis unavailable — fall back to synchronous in-process parsing ──
            _logger.LogWarning(ex,
                "DocumentParsingQueueService: Redis unavailable. Falling back to synchronous parsing. " +
                "DocumentId={DocumentId} DegradedMode=true",
                documentId);

            // Fire-and-forget on a background thread so the HTTP upload response is not delayed.
            // CancellationToken.None is intentional — the upload request's token may be disposed
            // before parsing completes (fire-and-forget lifetime is independent of the HTTP request).
            _ = Task.Run(async () =>
            {
                try
                {
                    await _parserWorker.ParseAsync(documentId, CancellationToken.None);
                }
                catch (Exception workerEx)
                {
                    _logger.LogError(workerEx,
                        "DocumentParsingQueueService: synchronous fallback parsing failed. DocumentId={DocumentId}",
                        documentId);
                }
            });
        }
    }
}
