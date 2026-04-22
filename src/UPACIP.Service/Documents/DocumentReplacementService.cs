using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Documents;

/// <summary>
/// Orchestrates the document replacement lifecycle (US_042 AC-2, AC-3, EC-1, EC-2).
///
/// Replacement flow:
///   1. Verify the previous document exists and is activatable (not already superseded).
///   2. Validate, encrypt, and store the replacement file using the shared storage service.
///   3. Create a new <c>ClinicalDocument</c> row linked to the previous version via
///      <c>PreviousVersionId</c>, with <c>VersionNumber = previous.VersionNumber + 1</c>.
///   4. Enqueue the replacement for AI parsing (reuses <see cref="IDocumentParsingQueueService"/>).
///   5. Return the new document ID so the caller can respond to SCR-012.
///
/// Activation (called after extraction succeeds):
///   6. Mark the old document as <c>Superseded</c> (ProcessingStatus + IsSuperseded flag).
///   7. Archive all extracted rows of the old version (IsArchived + ArchivedAtUtc).
///   8. Set <c>ReconsolidationNeeded = true</c> on the new active document (EC-2 signal).
///   All three mutations are committed in a single EF Core transaction (AC-3 atomicity).
///
/// Failure safety (EC-1):
///   If the replacement pipeline fails (parsing or extraction error), activation is never called.
///   The old version and its extracted rows remain fully active. The replacement file remains in
///   storage but the new document row is left in <c>Failed</c> state without affecting the old version.
/// </summary>
public sealed class DocumentReplacementService : IDocumentReplacementService
{
    private readonly ApplicationDbContext               _db;
    private readonly IClinicalDocumentUploadService     _uploadService;
    private readonly IDocumentParsingQueueService       _queue;
    private readonly ILogger<DocumentReplacementService> _logger;

    public DocumentReplacementService(
        ApplicationDbContext                db,
        IClinicalDocumentUploadService      uploadService,
        IDocumentParsingQueueService        queue,
        ILogger<DocumentReplacementService> logger)
    {
        _db            = db;
        _uploadService = uploadService;
        _queue         = queue;
        _logger        = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // StartReplacementAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<DocumentReplacementResult> StartReplacementAsync(
        Guid              previousDocumentId,
        Stream            fileStream,
        string            fileName,
        string            contentType,
        long              fileLength,
        Guid              patientId,
        DocumentCategory  category,
        Guid              uploaderUserId,
        string            uploaderDisplayName,
        CancellationToken cancellationToken = default)
    {
        // ── 1. Verify the previous document exists and is replaceable ────────────
        var previousDoc = await _db.ClinicalDocuments
            .FirstOrDefaultAsync(d => d.Id == previousDocumentId, cancellationToken);

        if (previousDoc is null)
        {
            throw new InvalidOperationException(
                $"Document not found: {previousDocumentId}. Cannot initiate replacement.");
        }

        if (previousDoc.IsSuperseded)
        {
            throw new InvalidOperationException(
                $"Document {previousDocumentId} is already superseded by a later replacement. " +
                "Only the currently active version can be replaced.");
        }

        if (previousDoc.PatientId != patientId)
        {
            // Security: prevent cross-patient replacement (OWASP A01).
            throw new InvalidOperationException(
                $"Document {previousDocumentId} does not belong to patient {patientId}.");
        }

        // ── 2. Validate and store the replacement file (reuse upload service) ──
        // UploadAsync validates format/size and handles encrypted storage + DB row creation.
        var uploadResult = await _uploadService.UploadAsync(
            fileStream,
            fileName,
            contentType,
            fileLength,
            patientId,
            category,
            uploaderUserId,
            uploaderDisplayName,
            cancellationToken);

        // ── 3. Link the new document version to the previous one ─────────────────
        // Load the newly created document row to set version lineage fields.
        var newDocument = await _db.ClinicalDocuments
            .FirstOrDefaultAsync(d => d.Id == uploadResult.DocumentId, cancellationToken);

        if (newDocument is null)
        {
            // Should not happen — UploadAsync guarantees persistence before returning.
            _logger.LogError(
                "DocumentReplacementService: newly uploaded document {DocumentId} not found after upload.",
                uploadResult.DocumentId);
            throw new InvalidOperationException(
                "Internal error: replacement document not found after upload.");
        }

        var versionNumber = previousDoc.VersionNumber + 1;
        newDocument.PreviousVersionId = previousDocumentId;
        newDocument.VersionNumber     = versionNumber;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "DocumentReplacementService: replacement started. " +
            "NewDocumentId={NewDocumentId} PreviousDocumentId={PreviousDocumentId} Version={Version}",
            newDocument.Id, previousDocumentId, versionNumber);

        return new DocumentReplacementResult
        {
            NewDocumentId      = newDocument.Id,
            PreviousDocumentId = previousDocumentId,
            VersionNumber      = versionNumber,
            FileName           = uploadResult.FileName,
            Category           = uploadResult.Category,
            UploadedAt         = uploadResult.UploadedAt,
            UploadedByName     = uploadResult.UploadedByName,
            Status             = uploadResult.Status,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ActivateReplacementAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task ActivateReplacementAsync(
        Guid              newDocumentId,
        CancellationToken cancellationToken = default)
    {
        // Load the replacement document with its previous-version link.
        var newDoc = await _db.ClinicalDocuments
            .FirstOrDefaultAsync(d => d.Id == newDocumentId, cancellationToken);

        if (newDoc is null || newDoc.PreviousVersionId is null)
        {
            _logger.LogWarning(
                "DocumentReplacementService: ActivateReplacement called for document {Id} " +
                "which is not a replacement version. Skipped.",
                newDocumentId);
            return;
        }

        var prevDocId = newDoc.PreviousVersionId.Value;

        var prevDoc = await _db.ClinicalDocuments
            .FirstOrDefaultAsync(d => d.Id == prevDocId, cancellationToken);

        if (prevDoc is null || prevDoc.IsSuperseded)
        {
            _logger.LogWarning(
                "DocumentReplacementService: previous document {PrevId} not found or already superseded. Skipped.",
                prevDocId);
            return;
        }

        var now = DateTime.UtcNow;

        // ── All activation mutations in a single transaction (AC-3 atomicity) ──
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Step A: Supersede the old document version.
            prevDoc.IsSuperseded      = true;
            prevDoc.SupersededAtUtc   = now;
            prevDoc.ProcessingStatus  = ProcessingStatus.Superseded;
            prevDoc.UpdatedAt         = now;

            // Step B: Archive all extracted rows from the old version.
            var oldRows = await _db.ExtractedData
                .Where(e => e.DocumentId == prevDocId && !e.IsArchived)
                .ToListAsync(cancellationToken);

            foreach (var row in oldRows)
            {
                row.IsArchived    = true;
                row.ArchivedAtUtc = now;
                row.UpdatedAt     = now;
            }

            // Step C: Signal reconsolidation needed on the new active document (EC-2).
            newDoc.ReconsolidationNeeded = true;
            newDoc.UpdatedAt             = now;

            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "DocumentReplacementService: activation complete. " +
                "NewDocumentId={NewId} SupersededDocumentId={PrevId} ArchivedRows={RowCount}",
                newDocumentId, prevDocId, oldRows.Count);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex,
                "DocumentReplacementService: activation transaction rolled back. " +
                "NewDocumentId={NewId} PreviousDocumentId={PrevId}",
                newDocumentId, prevDocId);
            throw;
        }
    }
}
