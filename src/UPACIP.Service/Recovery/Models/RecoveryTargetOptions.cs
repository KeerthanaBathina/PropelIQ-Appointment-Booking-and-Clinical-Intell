namespace UPACIP.Service.Recovery.Models;

/// <summary>
/// Strongly-typed configuration binding for the "RecoveryTargets" appsettings section.
///
/// Documents and enforces the RPO/RTO compliance targets required by:
///   - NFR-024: 1-hour RPO (Recovery Point Objective)
///   - NFR-025: 4-hour RTO (Recovery Time Objective)
///   - DR-026: Quarterly recovery testing
///   - DR-027: 15-minute WAL archival interval
///
/// Required appsettings.json section:
/// <code>
/// "RecoveryTargets": {
///   "RpoMinutes": 60,
///   "RtoMinutes": 240,
///   "WalArchiveIntervalMinutes": 15,
///   "BackupFrequencyHours": 24,
///   "MonitoringCheckIntervalMinutes": 30,
///   "QuarterlyTestAlertDaysBefore": 14,
///   "AlertEmail": "admin@clinic.com"
/// }
/// </code>
/// </summary>
public sealed class RecoveryTargetOptions
{
    public const string SectionName = "RecoveryTargets";

    /// <summary>
    /// Maximum acceptable data loss window in minutes (NFR-024). Default: 60 (1 hour).
    /// An RPO violation is raised when the time since the last WAL archive or backup
    /// exceeds this threshold.
    /// </summary>
    public int RpoMinutes { get; init; } = 60;

    /// <summary>
    /// Maximum acceptable downtime in minutes during a disaster recovery event (NFR-025).
    /// Default: 240 (4 hours). Used to validate runbook step estimates and actual
    /// restoration test durations.
    /// </summary>
    public int RtoMinutes { get; init; } = 240;

    /// <summary>
    /// Expected maximum interval between consecutive WAL archive operations in minutes
    /// (DR-027). Default: 15. Monitoring raises a warning when the last archived WAL
    /// segment is older than this threshold.
    /// </summary>
    public int WalArchiveIntervalMinutes { get; init; } = 15;

    /// <summary>
    /// Expected maximum interval between consecutive full backups in hours (DR-022).
    /// Default: 24 (daily). Monitoring raises a warning when the most recent completed
    /// backup is older than this threshold.
    /// </summary>
    public int BackupFrequencyHours { get; init; } = 24;

    /// <summary>
    /// How frequently the <c>RecoveryTargetMonitoringService</c> checks RPO/RTO compliance,
    /// in minutes. Default: 30. Must be less than <see cref="RpoMinutes"/> to detect
    /// violations promptly.
    /// </summary>
    public int MonitoringCheckIntervalMinutes { get; init; } = 30;

    /// <summary>
    /// Days before the quarterly test deadline at which the scheduler begins emitting
    /// DueSoon warnings (DR-026). Default: 14.
    /// </summary>
    public int QuarterlyTestAlertDaysBefore { get; init; } = 14;

    /// <summary>
    /// Administrator email address for RPO/RTO alert notifications.
    /// Default: "admin@clinic.com". Should be overridden in production configuration.
    /// </summary>
    public string AlertEmail { get; init; } = "admin@clinic.com";
}
