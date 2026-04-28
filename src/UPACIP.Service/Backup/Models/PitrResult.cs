namespace UPACIP.Service.Backup.Models;

/// <summary>
/// Result of a point-in-time recovery operation (US_090, AC-2, AC-3, AC-4).
/// Returned by <c>IPointInTimeRecoveryService.ExecuteRecoveryAsync</c> and
/// exposed via the admin API endpoint.
/// </summary>
public sealed record PitrResult
{
    /// <summary>True when recovery completed successfully and integrity validation passed.</summary>
    public required bool Success { get; init; }

    /// <summary>The recovery target timestamp requested by the admin.</summary>
    public required DateTime TargetTimestampUtc { get; init; }

    /// <summary>
    /// The actual recovery point achieved — may differ slightly from the target due to WAL granularity.
    /// Set to <see cref="DateTime.MinValue"/> when the recovery did not complete.
    /// </summary>
    public required DateTime ActualRecoveryPointUtc { get; init; }

    /// <summary>Filename of the base backup that was restored as the starting point for WAL replay.</summary>
    public required string BaseBackupUsed { get; init; }

    /// <summary>Count of WAL segments replayed from the base backup timestamp to the target.</summary>
    public required int WalSegmentsReplayed { get; init; }

    /// <summary>Total elapsed time for the full PITR pipeline (all phases).</summary>
    public required TimeSpan RecoveryDuration { get; init; }

    /// <summary>True when post-recovery row counts and checksums match expectations (AC-3).</summary>
    public required bool IntegrityValidationPassed { get; init; }

    /// <summary>Per-table row count comparison between the pre-recovery snapshot and the recovered database (AC-3).</summary>
    public required List<TableRowCountComparison> RowCountResults { get; init; }

    /// <summary>Per-table checksum comparison for the recovered database (AC-3).</summary>
    public required List<TableChecksumComparison> ChecksumResults { get; init; }

    /// <summary>
    /// Explains why PITR fell back to the base backup only (WAL corruption — edge case 1).
    /// Null when WAL replay completed normally.
    /// </summary>
    public string? FallbackReason { get; init; }

    /// <summary>
    /// If <c>true</c>, only pre-flight validation was performed (DryRun mode).
    /// The recovery was not actually executed.
    /// </summary>
    public required bool IsDryRun { get; init; }

    /// <summary>
    /// Human-readable feasibility assessment produced during dry-run or pre-flight.
    /// Null when <see cref="IsDryRun"/> is <c>false</c>.
    /// </summary>
    public string? FeasibilitySummary { get; init; }

    /// <summary>Recovery-level error message; null when all phases completed without error.</summary>
    public string? ErrorMessage { get; init; }
}
