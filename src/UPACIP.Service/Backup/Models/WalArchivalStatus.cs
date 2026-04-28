namespace UPACIP.Service.Backup.Models;

/// <summary>
/// Snapshot of WAL archival pipeline health, produced by
/// <c>WalArchivalMonitoringService</c> and consumed by health checks and
/// the PITR recovery service (US_090 task_002).
/// </summary>
public sealed record WalArchivalStatus
{
    /// <summary>
    /// True when archiving is active, no gaps detected, and no corruption found.
    /// </summary>
    public required bool IsHealthy { get; init; }

    /// <summary>Filename of the most recently archived WAL segment; null if no segments exist.</summary>
    public string? LastArchivedSegment { get; init; }

    /// <summary>UTC timestamp of the last archived WAL segment (via file system last-write time); null if none.</summary>
    public DateTime? LastArchivedAtUtc { get; init; }

    /// <summary>Minutes elapsed since the last archived segment. Returns <see cref="int.MaxValue"/> if no segments exist.</summary>
    public int MinutesSinceLastArchive { get; init; }

    /// <summary>Total count of WAL segment files currently in the archive directory.</summary>
    public int TotalArchivedSegments { get; init; }

    /// <summary>Number of missing segment sequence gaps detected in the archive.</summary>
    public int DetectedGaps { get; init; }

    /// <summary>Number of WAL segments that failed <c>pg_waldump</c> integrity validation.</summary>
    public int CorruptSegments { get; init; }

    /// <summary>Total byte size of all files in the WAL archive directory.</summary>
    public long ArchiveDirectorySizeBytes { get; init; }

    /// <summary>UTC timestamp when this status snapshot was captured.</summary>
    public required DateTime CapturedAtUtc { get; init; }
}
