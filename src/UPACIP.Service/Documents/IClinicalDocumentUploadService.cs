using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Documents;

/// <summary>
/// Orchestrates clinical document upload: server-side validation, AES-256 encryption,
/// <c>ClinicalDocument</c> persistence, and failure cleanup (US_038 AC-1–AC-5, EC-1, EC-2).
/// </summary>
public interface IClinicalDocumentUploadService
{
    /// <summary>
    /// Validates, encrypts, and persists a clinical document upload.
    ///
    /// Validation is performed before any I/O (AC-1, AC-5).
    /// Encryption and database write are performed atomically — the encrypted file is
    /// deleted if the database write fails (EC-1).
    ///
    /// Returns a populated result on success.
    /// Throws <see cref="DocumentValidationException"/> for validation failures (AC-5).
    /// Throws <see cref="OperationCanceledException"/> if <paramref name="cancellationToken"/> fires.
    /// </summary>
    /// <param name="fileStream">Readable stream of the file bytes.</param>
    /// <param name="fileName">Original filename submitted by the caller (display only).</param>
    /// <param name="contentType">MIME type reported by the caller for cross-check validation.</param>
    /// <param name="fileLength">File size in bytes for size validation before reading the stream.</param>
    /// <param name="patientId">Patient the document belongs to.</param>
    /// <param name="category">Clinical category for routing.</param>
    /// <param name="uploaderUserId">Authenticated staff user performing the upload (AC-4).</param>
    /// <param name="uploaderDisplayName">Display name used in the response attribution row (AC-4).</param>
    /// <param name="cancellationToken">Caller-supplied cancellation token.</param>
    Task<ClinicalDocumentUploadResult> UploadAsync(
        Stream            fileStream,
        string            fileName,
        string            contentType,
        long              fileLength,
        Guid              patientId,
        DocumentCategory  category,
        Guid              uploaderUserId,
        string            uploaderDisplayName,
        CancellationToken cancellationToken = default);
}
