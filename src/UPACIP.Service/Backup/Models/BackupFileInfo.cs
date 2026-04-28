namespace UPACIP.Service.Backup.Models;

/// <summary>
/// Retention tier classification for a backup file (US_088, AC-2, DR-023).
///
/// Tiers are mutually exclusive — a file is assigned the <b>highest</b> applicable tier
/// so it receives the longest retention period:
/// <list type="bullet">
///   <item><see cref="Monthly"/> — backup taken on the configured day of month (default: 1st). 365-day retention.</item>
///   <item><see cref="Weekly"/>  — backup taken on the configured day of week (default: Sunday). 90-day retention.</item>
///   <item><see cref="Daily"/>   — all other backups. 30-day retention.</item>
/// </list>
///
/// Example: a backup on Sunday January 1st qualifies for both Monthly and Weekly.
/// It is classified as <see cref="Monthly"/> and retained for 365 days.
/// </summary>
public enum BackupTier
{
    /// <summary>Standard daily backup — 30-day retention.</summary>
    Daily = 0,

    /// <summary>Weekly backup (Sunday by default) — 90-day retention.</summary>
    Weekly = 1,

    /// <summary>Monthly backup (1st of month by default) — 365-day retention.</summary>
    Monthly = 2,
}

/// <summary>
/// Parsed representation of a backup file on disk, enriched with tier classification
/// and expiration status (US_088, AC-2).
///
/// Created by <c>BackupRetentionService</c> during the directory scan phase.
/// </summary>
public sealed record BackupFileInfo
{
    /// <summary>Filename only (e.g. <c>upacip_backup_20260428_020000.dump</c>).</summary>
    public required string FileName { get; init; }

    /// <summary>Absolute file-system path to the backup file.</summary>
    public required string FullPath { get; init; }

    /// <summary>Timestamp parsed from the filename using the <c>yyyyMMdd_HHmmss</c> format.</summary>
    public required DateTime BackupTimestamp { get; init; }

    /// <summary>File size in bytes at the time of the directory scan.</summary>
    public long FileSizeBytes { get; init; }

    /// <summary>
    /// Retention tier — the highest applicable tier for this backup's timestamp.
    /// Determines the retention period applied by the cleanup logic.
    /// </summary>
    public required BackupTier Tier { get; init; }

    /// <summary>
    /// Age of the backup file in whole days from <c>DateTime.UtcNow</c> at scan time.
    /// </summary>
    public int AgeDays { get; init; }

    /// <summary>
    /// <c>true</c> when <see cref="AgeDays"/> exceeds the retention period for
    /// <see cref="Tier"/>; <c>false</c> if the file should be retained.
    /// </summary>
    public bool IsExpired { get; init; }
}
