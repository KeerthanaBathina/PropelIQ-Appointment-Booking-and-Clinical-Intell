namespace UPACIP.Service.Documents;

/// <summary>
/// Transport-agnostic AES-256-CBC encryption service for clinical document streams
/// (US_063 AC-2, HIPAA §164.312(a)(2)(iv), FR-091, NFR-009).
///
/// Implementations must:
///   - Generate a cryptographically random 16-byte IV per <see cref="EncryptAsync"/> call.
///   - Prepend the IV to the ciphertext so <see cref="DecryptAsync"/> can recover it.
///   - Use a 256-bit (32-byte) AES key from secure configuration — never hardcoded.
///   - Never write key material, plaintext, or IV to logs or exception messages.
///
/// File format produced by <see cref="EncryptAsync"/>:
///   [ IV (16 bytes) ][ AES-256-CBC ciphertext (variable length) ]
/// </summary>
public interface IFileEncryptionService
{
    /// <summary>
    /// Encrypts all bytes from <paramref name="plaintext"/> and writes
    /// [ IV (16 bytes) ][ ciphertext ] to <paramref name="ciphertext"/>.
    /// A fresh CSPRNG IV is generated for every call — IVs are never reused.
    /// </summary>
    /// <param name="plaintext">Readable source stream of unencrypted bytes.</param>
    /// <param name="ciphertext">Writable destination stream for IV + ciphertext.</param>
    /// <param name="ct">Cancellation token.</param>
    Task EncryptAsync(Stream plaintext, Stream ciphertext, CancellationToken ct = default);

    /// <summary>
    /// Reads [ IV (16 bytes) ][ ciphertext ] from <paramref name="ciphertext"/> and writes
    /// the decrypted plaintext to <paramref name="plaintext"/>.
    /// </summary>
    /// <param name="ciphertext">Readable source stream containing IV + ciphertext.</param>
    /// <param name="plaintext">Writable destination stream for decrypted bytes.</param>
    /// <param name="ct">Cancellation token.</param>
    Task DecryptAsync(Stream ciphertext, Stream plaintext, CancellationToken ct = default);
}
