namespace UPACIP.Service.Recovery.Models;

/// <summary>
/// Immutable snapshot of the system's current RPO/RTO compliance state, produced by
/// <c>RecoveryTargetMonitoringService</c> on each monitoring cycle and cached in-memory
/// for low-latency API responses (US_095, AC-3, NFR-024, NFR-025).
///
/// A snapshot is replaced atomically (volatile reference swap) on each check so that
/// the admin API always returns the latest assessment without database round-trips.
/// </summary>
public sealed class RecoveryTargetStatus
{
    // ── RPO (Recovery Point Objective) ───────────────────────────────────────

    /// <summary>
    /// True when the time elapsed since the most recent data protection event
    /// (WAL archive or backup) does not exceed <see cref="TargetRpo"/>.
    /// </summary>
    public required bool RpoCompliant { get; init; }

    /// <summary>
    /// Actual RPO: time elapsed since the most recent WAL archive timestamp.
    /// Compared against <see cref="TargetRpo"/> to determine compliance.
    /// </summary>
    public required TimeSpan CurrentRpo { get; init; }

    /// <summary>Configured RPO target (default: 1 hour per NFR-024).</summary>
    public required TimeSpan TargetRpo { get; init; }

    // ── RTO (Recovery Time Objective) ────────────────────────────────────────

    /// <summary>
    /// True when all RTO readiness conditions are met: recent backup exists,
    /// WAL archives are current, an Active runbook exists, and the quarterly
    /// restoration test is not overdue.
    /// </summary>
    public required bool RtoCompliant { get; init; }

    /// <summary>Configured RTO target (default: 4 hours per NFR-025).</summary>
    public required TimeSpan TargetRto { get; init; }

    // ── Data protection timestamps ────────────────────────────────────────────

    /// <summary>UTC timestamp of the most recent successful backup.</summary>
    public required DateTime LastBackupUtc { get; init; }

    /// <summary>UTC timestamp of the most recent WAL archive segment.</summary>
    public required DateTime LastWalArchiveUtc { get; init; }

    // ── Quarterly test tracking ───────────────────────────────────────────────

    /// <summary>UTC timestamp of the most recent quarterly restoration test, or null if none.</summary>
    public DateTime? LastRecoveryTestUtc { get; init; }

    /// <summary>
    /// Deadline by which the next quarterly test must be executed (end of current quarter,
    /// per DR-026).
    /// </summary>
    public required DateTime NextRecoveryTestDeadlineUtc { get; init; }

    /// <summary>
    /// Current quarterly test compliance status:
    /// <list type="bullet">
    ///   <item><term>Current</term><description>Test has been run this quarter, or deadline is far away.</description></item>
    ///   <item><term>DueSoon</term><description>No test this quarter; deadline is within the alert window.</description></item>
    ///   <item><term>Overdue</term><description>Quarterly deadline has passed without a test.</description></item>
    /// </list>
    /// </summary>
    public required string QuarterlyTestStatus { get; init; }

    // ── Warnings ─────────────────────────────────────────────────────────────

    /// <summary>
    /// List of compliance issues detected during this check cycle.
    /// Empty when both RPO and RTO are fully compliant.
    /// </summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    /// <summary>UTC timestamp when this snapshot was produced by the monitoring service.</summary>
    public required DateTime CheckedAtUtc { get; init; }
}
