namespace UPACIP.Service.Documents;

/// <summary>
/// Abstracts AES-256 encrypted file write and cleanup operations so the upload service
/// is not coupled to the physical I/O implementation (US_038 AC-2).
///
/// All implementations must:
/// - Prepend a per-file random IV to the ciphertext before writing (defense-in-depth; AC-2).
/// - Never return or log the raw encryption key or unencrypted bytes.
/// - Delete the encrypted artifact atomically on failure to prevent partial files at rest (EC-1).
/// </summary>
public interface IEncryptedFileStorageService
{
    /// <summary>
    /// Reads <paramref name="source"/> in full, encrypts it with AES-256-CBC using a randomly
    /// generated IV, and writes IV + ciphertext to a new file under <paramref name="patientId"/>
    /// sub-directory.
    /// </summary>
    /// <param name="source">Readable stream of the plaintext file bytes.</param>
    /// <param name="patientId">Used to derive the per-patient storage sub-directory.</param>
    /// <param name="cancellationToken">Caller-supplied cancellation token.</param>
    /// <returns>
    /// Relative path (relative to the configured storage root) of the encrypted file.
    /// Stored in <c>ClinicalDocument.FilePath</c> for later retrieval.
    /// </returns>
    Task<string> WriteEncryptedAsync(
        Stream            source,
        Guid              patientId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads and decrypts the file at <paramref name="relativePath"/> (relative to storage root).
    /// Returns the plaintext bytes. Throws <see cref="FileNotFoundException"/> if the file does not exist.
    /// Used by the AI parsing worker to read document content for extraction.
    /// </summary>
    /// <param name="relativePath">Relative path stored in <c>ClinicalDocument.FilePath</c>.</param>
    /// <param name="cancellationToken">Caller-supplied cancellation token.</param>
    Task<byte[]> ReadDecryptedAsync(
        string            relativePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads and decrypts the file at <paramref name="relativePath"/> and returns a readable
    /// <see cref="Stream"/> of the plaintext bytes for preview streaming (US_042 EC-2).
    ///
    /// The caller is responsible for disposing the returned stream.
    /// Raw storage paths are never leaked outside the service layer — only controller-issued
    /// relative API URLs are returned to clients (EC-2).
    /// Throws <see cref="FileNotFoundException"/> if the file does not exist.
    /// </summary>
    /// <param name="relativePath">Relative path stored in <c>ClinicalDocument.FilePath</c>.</param>
    /// <param name="cancellationToken">Caller-supplied cancellation token.</param>
    /// <returns>A readable, seekable <see cref="MemoryStream"/> containing the decrypted bytes.</returns>
    Task<Stream> ReadDecryptedStreamAsync(
        string            relativePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the encrypted file at <paramref name="relativePath"/> (relative to storage root).
    /// A no-op if the file does not exist; never throws for missing files.
    /// Called when the database write fails after the file was stored (EC-1).
    /// </summary>
    /// <param name="relativePath">Relative path returned by <see cref="WriteEncryptedAsync"/>.</param>
    void DeleteIfExists(string relativePath);
}
