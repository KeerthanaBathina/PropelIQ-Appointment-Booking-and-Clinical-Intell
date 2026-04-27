using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace UPACIP.Service.Documents;

/// <summary>
/// AES-256-CBC file encryption service (US_063 AC-2, HIPAA §164.312(a)(2)(iv), FR-091, NFR-009).
///
/// File format produced by <see cref="EncryptAsync"/>:
///   [ IV (16 bytes) ][ AES-256-CBC / PKCS7 ciphertext ]
///
/// Security properties (OWASP A02 — Cryptographic Failures):
///   - 256-bit AES key loaded from <see cref="EncryptionOptions.Key"/> — never hardcoded.
///   - Fresh CSPRNG IV (<see cref="RandomNumberGenerator.GetBytes"/>) per call — no IV reuse.
///   - Key length is validated to exactly 32 bytes at construction time (fail-fast on startup).
///   - Key material, plaintext, and IV are never written to logs or exception messages.
///
/// Registered as <c>Singleton</c> in the DI container — stateless crypto; safe to share.
/// </summary>
public sealed class FileEncryptionService : IFileEncryptionService
{
    private const int IvSizeBytes    = 16;
    private const int AesKeySizeBytes = 32; // 256-bit

    private readonly byte[]                         _key;
    private readonly ILogger<FileEncryptionService> _logger;

    public FileEncryptionService(
        IOptions<EncryptionOptions>         options,
        ILogger<FileEncryptionService>      logger)
    {
        _logger = logger;

        var opts = options.Value;

        if (string.IsNullOrWhiteSpace(opts.Key))
            throw new InvalidOperationException(
                "Security:Encryption:Key is required. Generate with: openssl rand -base64 32 " +
                "and store in user secrets or the production secrets manager.");

        byte[] key;
        try
        {
            key = Convert.FromBase64String(opts.Key);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "Security:Encryption:Key is not valid Base64.");
        }

        if (key.Length != AesKeySizeBytes)
            throw new InvalidOperationException(
                $"Security:Encryption:Key must decode to exactly {AesKeySizeBytes} bytes (AES-256). " +
                $"Got {key.Length} bytes. Re-generate with: openssl rand -base64 32");

        _key = key;

        _logger.LogInformation(
            "FileEncryptionService initialized. Algorithm={Algorithm} KeyVersion={KeyVersion}",
            opts.Algorithm, opts.KeyVersion);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IFileEncryptionService
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task EncryptAsync(
        Stream            plaintext,
        Stream            ciphertext,
        CancellationToken ct = default)
    {
        // Generate a fresh CSPRNG IV for every encryption call (OWASP A02 — no IV reuse).
        var iv = RandomNumberGenerator.GetBytes(IvSizeBytes);

        // Write IV prefix so DecryptAsync can recover it.
        await ciphertext.WriteAsync(iv, ct);

        using var aes   = CreateAes(iv);
        using var enc   = aes.CreateEncryptor();
        await using var crypto = new CryptoStream(ciphertext, enc, CryptoStreamMode.Write, leaveOpen: true);

        await plaintext.CopyToAsync(crypto, ct);
        await crypto.FlushFinalBlockAsync(ct);

        _logger.LogDebug("FileEncryptionService: EncryptAsync completed. CiphertextBytes={Bytes}",
            ciphertext.CanSeek ? ciphertext.Position : -1L);
    }

    /// <inheritdoc />
    public async Task DecryptAsync(
        Stream            ciphertext,
        Stream            plaintext,
        CancellationToken ct = default)
    {
        // Read the IV from the stream prefix.
        var iv        = new byte[IvSizeBytes];
        var bytesRead = await ciphertext.ReadAsync(iv.AsMemory(0, IvSizeBytes), ct);

        if (bytesRead != IvSizeBytes)
            throw new InvalidDataException(
                "Encrypted stream is too short to contain a valid IV prefix. " +
                "The file may be corrupt or was not produced by FileEncryptionService.");

        using var aes   = CreateAes(iv);
        using var dec   = aes.CreateDecryptor();
        await using var crypto = new CryptoStream(ciphertext, dec, CryptoStreamMode.Read, leaveOpen: true);

        await crypto.CopyToAsync(plaintext, ct);

        _logger.LogDebug("FileEncryptionService: DecryptAsync completed. PlaintextBytes={Bytes}",
            plaintext.CanSeek ? plaintext.Position : -1L);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private Aes CreateAes(byte[] iv)
    {
        var aes = Aes.Create();
        aes.Key     = _key;
        aes.IV      = iv;
        aes.Mode    = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        return aes;
    }
}
