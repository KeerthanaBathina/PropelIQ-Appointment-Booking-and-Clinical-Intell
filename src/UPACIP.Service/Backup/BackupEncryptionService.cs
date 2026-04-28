using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.Service.Backup.Models;

namespace UPACIP.Service.Backup;

// ─────────────────────────────────────────────────────────────────────────────
// Result DTO
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Metadata returned by <see cref="IBackupEncryptionService.EncryptBackupAsync"/>
/// (US_089, AC-1).
/// </summary>
public sealed record EncryptionResult
{
    /// <summary>Full path to the encrypted <c>.dump.enc</c> output file.</summary>
    public required string EncryptedFilePath { get; init; }

    /// <summary>Size of the encrypted output file in bytes.</summary>
    public long EncryptedFileSizeBytes { get; init; }

    /// <summary>SHA-256 hex checksum of the encrypted output file.</summary>
    public required string Checksum { get; init; }

    /// <summary>Size of the original plaintext <c>.dump</c> file before encryption.</summary>
    public long OriginalFileSizeBytes { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Interface
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Encrypts and decrypts pg_dump backup files using AES-256-CBC (US_089, AC-1, DR-025).
/// Registered as a Singleton — stateless beyond injected options.
/// </summary>
public interface IBackupEncryptionService
{
    /// <summary>
    /// Encrypts the plaintext <paramref name="plaintextFilePath"/> using AES-256-CBC,
    /// writes the output to <c>&lt;plaintextFilePath&gt;.enc</c>, deletes the plaintext
    /// file, and returns metadata about the encrypted artifact.
    /// </summary>
    Task<EncryptionResult> EncryptBackupAsync(
        string            plaintextFilePath,
        CancellationToken ct = default);

    /// <summary>
    /// Decrypts <paramref name="encryptedFilePath"/> to <paramref name="outputPath"/>
    /// using AES-256-CBC.  Used by the restoration testing framework (US_089 task_003).
    /// </summary>
    Task<string> DecryptBackupAsync(
        string            encryptedFilePath,
        string            outputPath,
        CancellationToken ct = default);
}

// ─────────────────────────────────────────────────────────────────────────────
// Implementation
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Singleton implementation of <see cref="IBackupEncryptionService"/>.
///
/// Algorithm: AES-256-CBC with PKCS7 padding.
/// IV: 128-bit (16-byte) cryptographically random value generated per operation.
/// IV storage: prepended as the first 16 bytes of the <c>.dump.enc</c> file so
///             decryption does not require separate IV management.
///
/// File lifecycle:
/// <list type="number">
///   <item>BackupExecutor produces a plaintext <c>upacip_backup_*.dump</c> file.</item>
///   <item>EncryptBackupAsync reads it via streaming (80 KB buffer), writes <c>*.dump.enc</c>.</item>
///   <item>The plaintext <c>.dump</c> file is deleted immediately after successful encryption.</item>
///   <item>Only the <c>.dump.enc</c> file is retained in backup storage.</item>
/// </list>
///
/// Security:
/// <list type="bullet">
///   <item>Key is never logged, printed, or included in any diagnostic output.</item>
///   <item>Key is sourced from configuration (env var in production — never alongside backups).</item>
///   <item>IV is unique per file — same key never produces the same ciphertext for identical input.</item>
/// </list>
/// </summary>
public sealed class BackupEncryptionService : IBackupEncryptionService
{
    // AES-256 constants
    private const int IvSizeBytes  = 16;  // 128-bit IV
    private const int KeySizeBytes = 32;  // 256-bit key

    private readonly IOptionsMonitor<EncryptionOptions> _options;
    private readonly ILogger<BackupEncryptionService>   _logger;

    public BackupEncryptionService(
        IOptionsMonitor<EncryptionOptions> options,
        ILogger<BackupEncryptionService>   logger)
    {
        _options = options;
        _logger  = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Encryption
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<EncryptionResult> EncryptBackupAsync(
        string            plaintextFilePath,
        CancellationToken ct = default)
    {
        var opts           = _options.CurrentValue;
        var key            = ValidateAndDecodeKey(opts.EncryptionKeyBase64);
        var encryptedPath  = plaintextFilePath + ".enc";
        var originalSize   = new FileInfo(plaintextFilePath).Length;

        // Generate a fresh cryptographically random IV for this operation.
        // A new IV per file ensures the same plaintext never produces the same ciphertext.
        var iv = new byte[IvSizeBytes];
        RandomNumberGenerator.Fill(iv);

        using var aes = CreateAes(key, iv);

        await using (var plaintextStream = new FileStream(
            plaintextFilePath,
            FileMode.Open, FileAccess.Read, FileShare.Read,
            opts.BufferSizeBytes, useAsync: true))
        await using (var encryptedStream = new FileStream(
            encryptedPath,
            FileMode.Create, FileAccess.Write, FileShare.None,
            opts.BufferSizeBytes, useAsync: true))
        {
            // Prepend the 16-byte IV — decryption reads this before creating the transform.
            await encryptedStream.WriteAsync(iv, ct);

            await using var cryptoStream = new CryptoStream(
                encryptedStream,
                aes.CreateEncryptor(),
                CryptoStreamMode.Write,
                leaveOpen: false);

            await CopyStreamAsync(plaintextStream, cryptoStream, opts.BufferSizeBytes, ct);
            await cryptoStream.FlushFinalBlockAsync(ct);
        }

        // Compute checksum over the encrypted output — integrity covers the stored artifact.
        var checksum      = await ComputeChecksumAsync(encryptedPath, opts.BufferSizeBytes, ct);
        var encryptedSize = new FileInfo(encryptedPath).Length;

        // Delete the plaintext file — only the encrypted file is retained in storage.
        File.Delete(plaintextFilePath);

        _logger.LogInformation(
            "BACKUP_ENCRYPTED: File={EncryptedFile}, " +
            "OriginalSize={OriginalBytes}, EncryptedSize={EncryptedBytes}, " +
            "Checksum={Checksum}",
            Path.GetFileName(encryptedPath),
            originalSize,
            encryptedSize,
            checksum);

        return new EncryptionResult
        {
            EncryptedFilePath     = encryptedPath,
            EncryptedFileSizeBytes = encryptedSize,
            Checksum              = checksum,
            OriginalFileSizeBytes = originalSize,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Decryption
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<string> DecryptBackupAsync(
        string            encryptedFilePath,
        string            outputPath,
        CancellationToken ct = default)
    {
        var opts = _options.CurrentValue;
        var key  = ValidateAndDecodeKey(opts.EncryptionKeyBase64);

        await using var encryptedStream = new FileStream(
            encryptedFilePath,
            FileMode.Open, FileAccess.Read, FileShare.Read,
            opts.BufferSizeBytes, useAsync: true);

        // Read the IV from the first 16 bytes of the encrypted file.
        var iv = new byte[IvSizeBytes];
        int read = 0;
        while (read < IvSizeBytes)
        {
            int n = await encryptedStream.ReadAsync(iv.AsMemory(read, IvSizeBytes - read), ct);
            if (n == 0)
                throw new InvalidOperationException(
                    $"Encrypted backup file is too short to contain a valid IV: {encryptedFilePath}");
            read += n;
        }

        using var aes = CreateAes(key, iv);

        await using var cryptoStream = new CryptoStream(
            encryptedStream,
            aes.CreateDecryptor(),
            CryptoStreamMode.Read,
            leaveOpen: false);

        await using var outputStream = new FileStream(
            outputPath,
            FileMode.Create, FileAccess.Write, FileShare.None,
            opts.BufferSizeBytes, useAsync: true);

        await CopyStreamAsync(cryptoStream, outputStream, opts.BufferSizeBytes, ct);

        _logger.LogInformation(
            "BACKUP_DECRYPTED: Source={EncryptedFile}, Output={DecryptedFile}",
            Path.GetFileName(encryptedFilePath),
            outputPath);

        return outputPath;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Decodes and validates the Base64 encryption key.
    /// Throws <see cref="InvalidOperationException"/> if the key is missing or not 32 bytes.
    /// The key value is intentionally excluded from the exception message.
    /// </summary>
    private static byte[] ValidateAndDecodeKey(string keyBase64)
    {
        if (string.IsNullOrWhiteSpace(keyBase64))
            throw new InvalidOperationException(
                "Backup encryption key is not configured. " +
                "Set BackupEncryption__EncryptionKeyBase64 via environment variable.");

        byte[] key;
        try
        {
            key = Convert.FromBase64String(keyBase64);
        }
        catch (FormatException)
        {
            throw new InvalidOperationException(
                "Backup encryption key is not a valid Base64 string.");
        }

        if (key.Length != KeySizeBytes)
            throw new InvalidOperationException(
                $"Encryption key must be exactly 256 bits ({KeySizeBytes} bytes). " +
                $"Decoded length was {key.Length} bytes.");

        return key;
    }

    /// <summary>Creates and configures an AES-256-CBC instance.</summary>
    private static Aes CreateAes(byte[] key, byte[] iv)
    {
        var aes = Aes.Create();
        aes.KeySize = 256;
        aes.Mode    = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.Key     = key;
        aes.IV      = iv;
        return aes;
    }

    /// <summary>
    /// Streams <paramref name="source"/> into <paramref name="destination"/> using the
    /// specified buffer size. Avoids loading large backup files into memory.
    /// </summary>
    private static async Task CopyStreamAsync(
        Stream            source,
        Stream            destination,
        int               bufferSize,
        CancellationToken ct)
    {
        var buffer = new byte[bufferSize];
        int bytesRead;
        while ((bytesRead = await source.ReadAsync(buffer, ct)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
        }
    }

    /// <summary>
    /// Streams the file through SHA-256 to produce a hex checksum without loading the
    /// entire file into memory.
    /// </summary>
    private static async Task<string> ComputeChecksumAsync(
        string            filePath,
        int               bufferSize,
        CancellationToken ct)
    {
        using var sha256 = SHA256.Create();
        await using var stream = new FileStream(
            filePath,
            FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize, useAsync: true);

        var hashBytes = await sha256.ComputeHashAsync(stream, ct);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
