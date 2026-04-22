using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UPACIP.Service.Documents;

/// <summary>
/// AES-256-CBC encrypted file storage for clinical documents (US_038 AC-2).
///
/// File format written to disk: [ IV (16 bytes) ][ AES-CBC ciphertext ]
/// The IV is unique per file; it is not secret but must be stored with the ciphertext
/// for decryption. The encryption key is loaded from configuration, never hardcoded.
///
/// Security notes (OWASP A02 — Cryptographic Failures):
///   - AES-256 key must be 32 bytes exactly; startup validation enforces this.
///   - CBC mode with PKCS7 padding is used for broad .NET runtime compatibility.
///   - IV is generated via <see cref="RandomNumberGenerator.GetBytes"/> (CSPRNG).
///   - Key material is never written to logs, exception messages, or responses.
/// </summary>
public sealed class EncryptedFileStorageService : IEncryptedFileStorageService
{
    private const int IvSizeBytes = 16;
    private const int AesKeySizeBytes = 32; // 256-bit

    private readonly DocumentStorageSettings            _settings;
    private readonly ILogger<EncryptedFileStorageService> _logger;

    public EncryptedFileStorageService(
        IOptions<DocumentStorageSettings>          options,
        ILogger<EncryptedFileStorageService>        logger)
    {
        _settings = options.Value;
        _logger   = logger;
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
        var key         = Convert.FromBase64String(_settings.EncryptionKeyBase64);
        var iv          = RandomNumberGenerator.GetBytes(IvSizeBytes);
        var fileName    = $"{Guid.NewGuid():N}.enc";
        var relPath     = Path.Combine(patientId.ToString("N"), fileName);
        var absDir      = Path.Combine(_settings.StoragePath, patientId.ToString("N"));
        var absPath     = Path.Combine(absDir, fileName);

        Directory.CreateDirectory(absDir);

        await using var fileStream = new FileStream(
            absPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);

        // Write IV prefix so it can be recovered during decryption.
        await fileStream.WriteAsync(iv, cancellationToken);

        using var aes = Aes.Create();
        aes.Key     = key;
        aes.IV      = iv;
        aes.Mode    = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor   = aes.CreateEncryptor();
        await using var cryptoStream = new CryptoStream(fileStream, encryptor, CryptoStreamMode.Write, leaveOpen: false);

        await source.CopyToAsync(cryptoStream, cancellationToken);
        await cryptoStream.FlushFinalBlockAsync(cancellationToken);

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
        // Prevent path traversal (OWASP A01).
        var absPath     = Path.Combine(_settings.StoragePath, relativePath);
        var resolvedAbs = Path.GetFullPath(absPath);
        if (!resolvedAbs.StartsWith(Path.GetFullPath(_settings.StoragePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path traversal attempt detected in relativePath.");

        if (!File.Exists(resolvedAbs))
            throw new FileNotFoundException("Encrypted document file not found.", resolvedAbs);

        var key = Convert.FromBase64String(_settings.EncryptionKeyBase64);

        await using var fileStream = new FileStream(
            resolvedAbs, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, useAsync: true);

        // Read the IV from the file prefix.
        var iv = new byte[IvSizeBytes];
        var bytesRead = await fileStream.ReadAsync(iv.AsMemory(0, IvSizeBytes), cancellationToken);
        if (bytesRead != IvSizeBytes)
            throw new InvalidDataException("Encrypted file is too short to contain a valid IV.");

        using var aes = Aes.Create();
        aes.Key     = key;
        aes.IV      = iv;
        aes.Mode    = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var decryptor  = aes.CreateDecryptor();
        await using var cryptoStream = new CryptoStream(fileStream, decryptor, CryptoStreamMode.Read, leaveOpen: false);
        using var ms = new MemoryStream();
        await cryptoStream.CopyToAsync(ms, cancellationToken);
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

    private static void ValidateSettings(DocumentStorageSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.StoragePath))
            throw new InvalidOperationException(
                "DocumentStorage:StoragePath is required. Set it in appsettings.json or user secrets.");

        if (string.IsNullOrWhiteSpace(settings.EncryptionKeyBase64))
            throw new InvalidOperationException(
                "DocumentStorage:EncryptionKeyBase64 is required. Generate with: " +
                "Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))");

        byte[] key;
        try { key = Convert.FromBase64String(settings.EncryptionKeyBase64); }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "DocumentStorage:EncryptionKeyBase64 is not valid Base64.");
        }

        if (key.Length != AesKeySizeBytes)
            throw new InvalidOperationException(
                $"DocumentStorage:EncryptionKeyBase64 must decode to exactly {AesKeySizeBytes} bytes (AES-256). " +
                $"Got {key.Length} bytes.");
    }
}
