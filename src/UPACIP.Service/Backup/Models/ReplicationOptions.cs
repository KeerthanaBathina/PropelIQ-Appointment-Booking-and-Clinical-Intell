namespace UPACIP.Service.Backup.Models;

/// <summary>
/// Configuration for geographic backup replication to a Windows file share (DR-024).
/// Bound from the <c>"BackupReplication"</c> configuration section.
/// The remote path is supplied via environment variable
/// <c>BackupReplication__RemoteDestinationPath</c> in production — it is never committed
/// to source control (OWASP A02).
/// </summary>
public sealed class ReplicationOptions
{
    /// <summary>Configuration section key used with <see cref="IOptionsMonitor{T}"/>.</summary>
    public const string SectionName = "BackupReplication";

    /// <summary>
    /// UNC path to the geographically separate Windows file share
    /// (e.g., <c>\\dr-server\BackupShare\Database</c>).
    /// Required when <see cref="Enabled"/> is <c>true</c>.
    /// </summary>
    public string RemoteDestinationPath { get; init; } = string.Empty;

    /// <summary>
    /// Whether geographic replication is active.  Set to <c>false</c> in
    /// development/test environments where no remote share is available.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Maximum number of retry attempts after the initial copy failure.
    /// Edge case 1: 3 retries with exponential backoff (30 s → 120 s → 480 s).
    /// </summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Base delay in seconds for the first retry.  Subsequent retries double each time
    /// per <see cref="Polly.DelayBackoffType.Exponential"/>: 30 s → 120 s → 480 s.
    /// </summary>
    public int InitialRetryDelaySeconds { get; init; } = 30;

    /// <summary>Whether to verify the remote copy checksum after transfer.</summary>
    public bool VerifyChecksum { get; init; } = true;

    /// <summary>Maximum allowed duration (in minutes) for a single file copy before timeout.</summary>
    public int TransferTimeoutMinutes { get; init; } = 60;
}
