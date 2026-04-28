namespace UPACIP.Service.Backup.Models;

/// <summary>
/// Immutable result DTO returned by <c>IBackupExecutor.ExecuteBackupAsync</c>
/// (US_088, AC-3).
///
/// Contains all metadata needed to persist a <see cref="BackupLog"/> record and
/// emit the <c>BACKUP_COMPLETED</c> / <c>BACKUP_FAILED</c> structured log event.
/// </summary>
public sealed record BackupResult
{
    /// <summary>
    /// <c>true</c> if <c>pg_dump</c> exited with code 0; <c>false</c> otherwise.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Full file-system path to the <c>.dump</c> file.
    /// Set to an empty string on failure (file may not exist).
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Backup filename only (e.g. <c>upacip_backup_20260428_020000.dump</c>).
    /// Set to an empty string on failure.
    /// </summary>
    public required string FileName { get; init; }

    /// <summary>
    /// Backup file size in bytes. Zero on failure or when the file was not written.
    /// </summary>
    public long FileSizeBytes { get; init; }

    /// <summary>Elapsed time for the entire <c>pg_dump</c> execution.</summary>
    public required TimeSpan Duration { get; init; }

    /// <summary>
    /// SHA-256 hex string of the backup file content (AC-3 — checksum logging).
    /// Empty string on failure.
    /// </summary>
    public string Checksum { get; init; } = string.Empty;

    /// <summary>
    /// <c>pg_dump</c> stderr output on failure; <c>null</c> on success.
    /// Truncated to 2 000 characters to prevent log flooding.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// <c>true</c> when this result is from a retry attempt (AC-4).
    /// Persisted on the <see cref="BackupLog"/> for visibility.
    /// </summary>
    public bool IsRetry { get; init; }

    /// <summary>UTC timestamp when the backup process completed (or failed).</summary>
    public required DateTime CompletedAtUtc { get; init; }

    // ── Encryption metadata (US_089, AC-1, DR-025) ────────────────────────────

    /// <summary>
    /// Full path to the encrypted <c>.dump.enc</c> file.
    /// <c>null</c> when encryption is disabled or was not attempted.
    /// When set, this supersedes <see cref="FilePath"/> as the primary stored artifact.
    /// </summary>
    public string? EncryptedFilePath { get; init; }

    /// <summary>
    /// Encrypted file size in bytes.
    /// <c>null</c> when encryption is disabled or was not attempted.
    /// </summary>
    public long? EncryptedFileSizeBytes { get; init; }

    /// <summary>
    /// SHA-256 hex checksum of the encrypted file.
    /// Replaces <see cref="Checksum"/> as the stored-artifact integrity value when set.
    /// <c>null</c> when encryption is disabled or was not attempted.
    /// </summary>
    public string? EncryptedChecksum { get; init; }
}
