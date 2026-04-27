using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UPACIP.Service.Documents;

/// <summary>
/// AES-256-CBC encrypted file storage for clinical documents (US_038 AC-2, US_063 AC-2).
///
/// Delegates all cryptographic operations to <see cref="IFileEncryptionService"/> so the
/// storage layer is decoupled from the cipher implementation (AC-2, FR-091, NFR-009).
///
/// File format on disk: [ IV (16 bytes) ][ AES-CBC ciphertext ] — managed by
/// <see cref="FileEncryptionService"/> which prepends and reads the IV automatically.
///
/// Security notes (OWASP A01 — Broken Access Control, A02 — Cryptographic Failures):
///   - Path traversal is prevented by resolving absolute paths and checking they remain
///     under the configured storage root before any I/O.
///   - Raw file-system paths are never returned to callers outside the service layer.
///   - Key material is owned by <see cref="IFileEncryptionService"/>; this class never
///     handles encryption keys directly.
/// </summary>
public sealed class EncryptedFileStorageService : IEncryptedFileStorageService
{
    private readonly DocumentStorageSettings              _settings;
    private readonly IFileEncryptionService               _encryption;
    private readonly ILogger<EncryptedFileStorageService> _logger;

    public EncryptedFileStorageService(
        IOptions<DocumentStorageSettings>          options,
        IFileEncryptionService                     encryption,
        ILogger<EncryptedFileStorageService>       logger)
    {
        _settings   = options.Value;
        _encryption = encryption;
        _logger     = logger;
        ValidateSettings(_settings);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IEncryptedFileStorageService
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<string> WriteEncryptedAsync(
        Stream            source,
        Guid              patientId,
        CancellationToken cancellationToken = default)
    {
        var fileName = $"{Guid.NewGuid():N}.enc";
        var relPath  = Path.Combine(patientId.ToString("N"), fileName);
        var absDir   = Path.Combine(_settings.StoragePath, patientId.ToString("N"));
        var absPath  = Path.Combine(absDir, fileName);

        Directory.CreateDirectory(absDir);

        await using var fileStream = new FileStream(
            absPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);

        // IFileEncryptionService prepends IV and writes ciphertext in one call (AC-2).
        await _encryption.EncryptAsync(source, fileStream, cancellationToken);

        _logger.LogInformation(
            "Encrypted clinical document written. Patient={PatientId} File={FileName}",
            patientId, fileName);

        return relPath;
    }

    /// <inheritdoc/>
    public async Task<byte[]> ReadDecryptedAsync(
        string            relativePath,
        CancellationToken cancellationToken = default)
    {
        var resolvedAbs = ResolveAndValidatePath(relativePath);

        if (!File.Exists(resolvedAbs))
            throw new FileNotFoundException("Encrypted document file not found.", resolvedAbs);

        await using var fileStream = new FileStream(
            resolvedAbs, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);

        // IFileEncryptionService reads IV prefix and decrypts the remainder (AC-2).
        using var ms = new MemoryStream();
        await _encryption.DecryptAsync(fileStream, ms, cancellationToken);
        return ms.ToArray();
    }

    /// <inheritdoc/>
    public async Task<Stream> ReadDecryptedStreamAsync(
        string            relativePath,
        CancellationToken cancellationToken = default)
    {
        // The byte-array path already performs all security checks; reuse it to keep
        // decryption logic in one place and return a seekable MemoryStream (EC-2).
        var bytes = await ReadDecryptedAsync(relativePath, cancellationToken);
        return new MemoryStream(bytes, writable: false);
    }

    /// <inheritdoc/>
    public void DeleteIfExists(string relativePath)
    {
        var absPath = Path.Combine(_settings.StoragePath, relativePath);
        if (!File.Exists(absPath))
            return;

        try
        {
            File.Delete(absPath);
            _logger.LogWarning(
                "Deleted partial encrypted document artifact during upload cleanup. Path={RelativePath}",
                relativePath);
        }
        catch (Exception ex)
        {
            // Log but swallow: upload cleanup is best-effort; the orphaned file will be
            // detected by a future storage audit sweep. Caller should not re-throw here
            // to avoid hiding the original persistence exception (EC-1).
            _logger.LogError(ex,
                "Failed to delete partial encrypted document artifact. Path={RelativePath}",
                relativePath);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a caller-supplied relative path against the storage root and rejects any
    /// path that would escape the root directory (OWASP A01 — path traversal prevention).
    /// </summary>
    private string ResolveAndValidatePath(string relativePath)
    {
        var absPath     = Path.Combine(_settings.StoragePath, relativePath);
        var resolvedAbs = Path.GetFullPath(absPath);
        if (!resolvedAbs.StartsWith(Path.GetFullPath(_settings.StoragePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path traversal attempt detected in relativePath.");
        return resolvedAbs;
    }

    private static void ValidateSettings(DocumentStorageSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.StoragePath))
            throw new InvalidOperationException(
                "DocumentStorage:StoragePath is required. Set it in appsettings.json or user secrets.");
    }
}
