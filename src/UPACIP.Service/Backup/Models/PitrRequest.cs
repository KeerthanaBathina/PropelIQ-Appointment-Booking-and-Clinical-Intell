using System.ComponentModel.DataAnnotations;

namespace UPACIP.Service.Backup.Models;

/// <summary>
/// Admin request to initiate a point-in-time recovery operation (US_090, AC-2).
/// </summary>
public sealed record PitrRequest
{
    /// <summary>
    /// The exact UTC timestamp to recover the database to.
    /// Must be in the past and within the WAL retention window.
    /// </summary>
    [Required]
    public required DateTime TargetTimestampUtc { get; init; }

    /// <summary>Identity of the admin user initiating the recovery (for audit trail).</summary>
    [Required]
    [MaxLength(256)]
    public required string PerformedBy { get; init; }

    /// <summary>
    /// When <c>true</c>, perform pre-flight validation only — do not execute the actual recovery.
    /// Returns a feasibility assessment: which base backup would be used, WAL segment availability,
    /// estimated recovery duration, and any detected WAL gaps.
    /// </summary>
    public bool DryRun { get; init; } = false;

    /// <summary>
    /// Whether to run post-recovery row count and checksum validation (AC-3).
    /// Default: <c>true</c>.
    /// </summary>
    public bool ValidateIntegrity { get; init; } = true;
}
