using System.ComponentModel.DataAnnotations;

namespace UPACIP.Service.Documents;

/// <summary>
/// Strongly-typed configuration for the application-level AES-256 file encryption service
/// (US_063 AC-2, HIPAA §164.312(a)(2)(iv), FR-091, NFR-009).
///
/// Required configuration section:
/// <code>
/// "Security": {
///   "Encryption": {
///     "Key": "&lt;base64-encoded 32-byte AES-256 key&gt;",
///     "Algorithm": "AES-256-CBC",
///     "KeyVersion": 1
///   }
/// }
/// </code>
///
/// Key generation (run once, store result in user secrets / secrets manager):
/// <code>
/// dotnet user-secrets set "Security:Encryption:Key" \
///   "$(openssl rand -base64 32)"
/// </code>
///
/// Phase 1 key rotation procedure (manual, documented):
///   1. Generate a new 256-bit key:   openssl rand -base64 32
///   2. Increment KeyVersion in configuration.
///   3. Update Security:Encryption:Key to the new value.
///   4. Run the key-rotation migration script (scripts/rotate-encryption-key.ps1) which
///      reads all .enc files, decrypts with the OLD key, re-encrypts with the NEW key.
///   5. Verify a sample of re-encrypted files decrypts correctly with the new key.
///   6. Retire the old key from secrets manager after full verification.
///   Phase 2 will automate rotation via Azure Key Vault / AWS KMS key versioning.
/// </summary>
public sealed class EncryptionOptions
{
    public const string SectionName = "Security:Encryption";

    /// <summary>
    /// Base64-encoded 256-bit (32-byte) AES encryption key.
    /// Never hardcode or log this value.
    /// </summary>
    [Required]
    public string Key { get; init; } = string.Empty;

    /// <summary>
    /// Symmetric encryption algorithm identifier. Always "AES-256-CBC".
    /// Retained for future algorithm agility without a code change.
    /// </summary>
    public string Algorithm { get; init; } = "AES-256-CBC";

    /// <summary>
    /// Monotonically increasing integer identifying which key version encrypted a file.
    /// Increment on every key rotation. Used by the rotation script to locate files
    /// encrypted with an older key version that require re-encryption.
    /// </summary>
    public int KeyVersion { get; init; } = 1;
}
