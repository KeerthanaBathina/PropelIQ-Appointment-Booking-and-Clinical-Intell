namespace UPACIP.Service.Retention.Models;

/// <summary>
/// Structured result of a single appointment archival operation (US_086 AC-3, AC-5).
/// Emitted as a Serilog structured log event at the end of each archival step in the
/// nightly <c>DataRetentionService</c> cycle.
/// </summary>
public sealed record ArchivalResult
{
    /// <summary>
    /// Which category of appointments was processed:
    /// <see cref="RetentionCategory.Appointments"/> (3-year threshold, AC-3) or
    /// <see cref="RetentionCategory.CancelledAppointments"/> (1-year threshold, AC-5).
    /// </summary>
    public required RetentionCategory Category { get; init; }

    /// <summary>
    /// Number of appointments successfully moved to <c>archive.appointments</c> with a
    /// corresponding <c>ArchivedAppointmentReference</c> stub retained in the main table.
    /// </summary>
    public required int RecordsArchived { get; init; }

    /// <summary>
    /// Number of appointments skipped because an audit-log entry references them within
    /// the 7-year retention window (edge case 2).
    /// These appointments will be retried on the next nightly cycle.
    /// </summary>
    public required int RecordsSkippedAuditProtected { get; init; }

    /// <summary>
    /// Number of appointments skipped because they still have active
    /// <c>NotificationLog</c> rows referencing them via FK.
    /// The notification purge (step 1) will clear these on the next cycle.
    /// </summary>
    public required int RecordsSkippedActiveFk { get; init; }

    /// <summary>
    /// <c>AppointmentTime</c> of the oldest appointment that was archived.
    /// <c>null</c> when <see cref="RecordsArchived"/> is zero.
    /// </summary>
    public DateTime? OldestArchivedDate { get; init; }

    /// <summary>
    /// <c>AppointmentTime</c> of the most recent appointment that was archived.
    /// <c>null</c> when <see cref="RecordsArchived"/> is zero.
    /// </summary>
    public DateTime? NewestArchivedDate { get; init; }

    /// <summary>Wall-clock time to complete the full archival pass.</summary>
    public required TimeSpan ExecutionDuration { get; init; }
}
