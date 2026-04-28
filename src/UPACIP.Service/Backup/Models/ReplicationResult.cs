namespace UPACIP.Service.Backup.Models;

/// <summary>
/// Outcome of a single geographic replication attempt (AC-2, DR-024).
/// </summary>
public sealed record ReplicationResult
{
    /// <summary>True when the file was successfully copied and checksum verified.</summary>
    public required bool Success { get; init; }

    /// <summary>Full destination path on the remote share; null when replication was skipped or failed before copy.</summary>
    public string? DestinationPath { get; init; }

    /// <summary>Size in bytes of the file that was replicated.</summary>
    public long FileSizeBytes { get; init; }

    /// <summary>Elapsed time for the file transfer (excludes retry wait delays).</summary>
    public TimeSpan TransferDuration { get; init; }

    /// <summary>Error details when <see cref="Success"/> is false; null on success.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Total copy attempts used (1 = first try succeeded; up to <c>MaxRetries + 1</c>).
    /// </summary>
    public int AttemptsUsed { get; init; }
}
