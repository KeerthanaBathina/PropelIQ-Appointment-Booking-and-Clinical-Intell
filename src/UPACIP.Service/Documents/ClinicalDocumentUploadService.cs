using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Documents;

/// <summary>
/// Validates, encrypts, and persists clinical document uploads (US_038 AC-1â€“AC-5, EC-1, EC-2).
///
/// Upload workflow:
///   1. Validate extension, MIME type, and size (AC-1, AC-5) â€” no I/O occurs if invalid.
///   2. Encrypt the file via <see cref="IEncryptedFileStorageService"/> (AC-2).
///   3. Insert a <c>ClinicalDocument</c> row with status <c>Uploaded</c> (AC-2, AC-3, AC-4).
///   4. If the database write fails, delete the encrypted artifact and re-throw (EC-1).
///   5. Return persisted metadata for the SCR-012 attribution row (AC-4).
///
/// Atomicity guarantee: encrypted file + database row are consistent because the file is
/// deleted on any <c>DbUpdateException</c> before propagating the exception (EC-1).
///
/// Corrupt-content handling (EC-2): extension/MIME/size checks pass for well-formed but
/// internally corrupt files â€” those are accepted here and fail later in the AI parsing stage.
/// </summary>
public sealed class ClinicalDocumentUploadService : IClinicalDocumentUploadService
{
    // â”€â”€â”€ Validation constants (must match frontend documentUploadValidation.ts) â”€â”€â”€â”€â”€
    private const long MaxFileSizeBytes = 10L * 1024 * 1024; // 10 MB

    private static readonly IReadOnlySet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx", ".txt", ".png", ".jpg", ".jpeg",
    };

    /// <summary>
    /// Allowed MIME types by extension (defence against extension-spoofing).
    /// Only checked when the client sends a Content-Type header on the part.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string[]> AllowedMimeByExtension =
        new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"]  = ["application/pdf"],
            [".docx"] = ["application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                         "application/octet-stream"],
            [".txt"]  = ["text/plain"],
            [".png"]  = ["image/png"],
            [".jpg"]  = ["image/jpeg"],
            [".jpeg"] = ["image/jpeg"],
        };

    private readonly ApplicationDbContext               _db;
    private readonly IEncryptedFileStorageService       _storage;
    private readonly IDocumentParsingQueueService        _queue;
    private readonly ILogger<ClinicalDocumentUploadService> _logger;

    public ClinicalDocumentUploadService(
        ApplicationDbContext                    db,
        IEncryptedFileStorageService            storage,
        IDocumentParsingQueueService            queue,
        ILogger<ClinicalDocumentUploadService>  logger)
    {
        _db      = db;
        _storage = storage;
        _queue   = queue;
        _logger  = logger;
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // IClinicalDocumentUploadService
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    /// <inheritdoc/>
    public async Task<ClinicalDocumentUploadResult> UploadAsync(
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
        // â”€â”€ Step 1: Validate before any I/O (AC-1, AC-5) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        ValidateFile(fileName, contentType, fileLength);

        // Sanitize: store only the file name component, never a caller-supplied path.
        var originalFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(originalFileName))
            throw new DocumentValidationException("File name must not be empty.");

        // â”€â”€ Step 2: Encrypt and persist to disk (AC-2) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        string? relPath = null;
        try
        {
            relPath = await _storage.WriteEncryptedAsync(fileStream, patientId, cancellationToken);
        }
        catch (Exception ex) when (ex is not DocumentValidationException and not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Encrypted file write failed during upload. Patient={PatientId} File={FileName}",
                patientId, originalFileName);
            throw new InvalidOperationException("Document storage is temporarily unavailable. Please try again.", ex);
        }

        // â”€â”€ Step 3: Persist ClinicalDocument row (AC-2, AC-3, AC-4) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
        var now = DateTime.UtcNow;
        var document = new ClinicalDocument
        {
            PatientId        = patientId,
            DocumentCategory = category,
            OriginalFileName = originalFileName,
            ContentType      = contentType,
            FileSizeBytes    = fileLength,
            FilePath         = relPath,
            UploadDate       = now,
            UploaderUserId   = uploaderUserId,
            ProcessingStatus = ProcessingStatus.Uploaded,
            CreatedAt        = now,
            UpdatedAt        = now,
        };

        _db.ClinicalDocuments.Add(document);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // â”€â”€ EC-1: Database write failed â€” discard encrypted artifact â”€â”€â”€â”€â”€â”€â”€â”€â”€
            _storage.DeleteIfExists(relPath);
            _logger.LogError(ex,
                "ClinicalDocument persistence failed; partial encrypted file cleaned up. " +
                "Patient={PatientId} File={FileName}",
                patientId, originalFileName);
            throw;
        }

        _logger.LogInformation(
            "ClinicalDocument uploaded successfully. " +
            "DocumentId={DocumentId} Patient={PatientId} Category={Category} UploadedBy={UploaderId}",
            document.Id, patientId, category, uploaderUserId);

        // ── Step 4 (US_039 AC-1): Enqueue for AI parsing ────────────────────────
        // Queue failure must not fail the upload response - document is durably stored.
        // DocumentParsingQueueService handles EC-1 (Redis fallback) internally.
        try
        {
            await _queue.EnqueueAsync(document.Id, cancellationToken);
        }
        catch (Exception qex)
        {
            _logger.LogError(qex,
                "ClinicalDocumentUploadService: queue handoff failed for DocumentId={DocumentId}. " +
                "Document remains in Uploaded state; manual re-queue may be required.",
                document.Id);
            // Intentionally swallowed - the upload succeeded; parsing can be retried later.
        }

        // ── Step 5: Return attribution metadata for SCR-012 (AC-4) ───────────
        return new ClinicalDocumentUploadResult
        {
            DocumentId     = document.Id,
            FileName       = originalFileName,
            Category       = category.ToString(),
            UploadedAt     = now,
            UploadedByName = uploaderDisplayName,
            Status         = "Uploaded",
        };
    }

    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
    // Private helpers
    // â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

    private static void ValidateFile(string fileName, string contentType, long fileLength)
    {
        // Size check (AC-1, AC-5)
        if (fileLength == 0)
            throw new DocumentValidationException("The uploaded file is empty.");

        if (fileLength > MaxFileSizeBytes)
            throw new DocumentValidationException(
                $"File exceeds the 10 MB limit. Received {fileLength / (1024d * 1024):F1} MB. " +
                "Reduce the file size and try again.");

        // Extension check (AC-1, AC-5)
        var ext = Path.GetExtension(fileName);
        if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext))
            throw new DocumentValidationException(
                $"Unsupported file type '{ext}'. " +
                $"Supported formats: {string.Join(", ", AllowedExtensions)}.");

        // MIME type check â€” only when the client provides Content-Type on the part.
        // EC-2: We do NOT inspect bytes, so corrupt files that pass these checks are accepted.
        if (!string.IsNullOrWhiteSpace(contentType) && contentType != "application/octet-stream")
        {
            if (AllowedMimeByExtension.TryGetValue(ext, out var allowedMimes)
                && !allowedMimes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            {
                throw new DocumentValidationException(
                    $"Content-Type '{contentType}' does not match the expected types for '{ext}'. " +
                    $"Supported formats: {string.Join(", ", AllowedExtensions)}.");
            }
        }
    }
}

