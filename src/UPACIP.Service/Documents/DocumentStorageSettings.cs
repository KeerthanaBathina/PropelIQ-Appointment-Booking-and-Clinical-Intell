namespace UPACIP.Service.Documents;

/// <summary>
/// Strongly-typed binding for the DocumentStorage section in appsettings.json.
///
/// Required appsettings.json section:
/// <code>
/// "DocumentStorage": {
///   "StoragePath": "C:\\UploadedDocuments"
/// }
/// </code>
///
/// Note: the AES-256 encryption key is configured separately under
/// <c>Security:Encryption:Key</c> via <see cref="EncryptionOptions"/>.
/// </summary>
public sealed class DocumentStorageSettings
{
    public const string SectionName = "DocumentStorage";

    /// <summary>
    /// Absolute path to the root directory where encrypted document files are written.
    /// Per-patient subdirectories are created automatically beneath this root.
    /// </summary>
    public string StoragePath { get; init; } = string.Empty;
}
