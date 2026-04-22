namespace UPACIP.Service.Documents;

/// <summary>
/// Strongly-typed binding for the DocumentStorage section in appsettings.json.
/// Encryption key is never logged (OWASP A02 — Cryptographic Failures).
///
/// Required appsettings.json section:
/// <code>
/// "DocumentStorage": {
///   "StoragePath": "C:\\UploadedDocuments",
///   "EncryptionKeyBase64": "&lt;base64-encoded 32-byte AES-256 key&gt;"
/// }
/// </code>
/// </summary>
public sealed class DocumentStorageSettings
{
    public const string SectionName = "DocumentStorage";

    /// <summary>
    /// Absolute path to the root directory where encrypted document files are written.
    /// Per-patient subdirectories are created automatically beneath this root.
    /// </summary>
    public string StoragePath { get; init; } = string.Empty;

    /// <summary>
    /// Base64-encoded 256-bit (32-byte) AES key used for all file encryption operations.
    /// Never hardcode; load from environment variable or secrets manager.
    /// </summary>
    public string EncryptionKeyBase64 { get; init; } = string.Empty;
}
