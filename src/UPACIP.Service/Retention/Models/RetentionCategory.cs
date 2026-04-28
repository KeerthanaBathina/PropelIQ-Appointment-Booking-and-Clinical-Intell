namespace UPACIP.Service.Retention.Models;

/// <summary>
/// Identifies the data category a retention policy rule applies to (US_086, DR-016–DR-020).
/// Used as the key for per-category retention periods and as a structured log field in
/// purge operation summaries.
/// </summary>
public enum RetentionCategory
{
    /// <summary>
    /// Immutable audit trail entries (<c>AuditLog</c> table).
    /// Minimum retention: 7 years (DR-016, HIPAA 45 CFR § 164.530(j)).
    /// Never automatically deleted — AC-1.
    /// </summary>
    AuditLogs,

    /// <summary>
    /// Clinical records: <c>ClinicalDocument</c>, <c>ExtractedData</c>, <c>MedicalCode</c>.
    /// Retained indefinitely — never automatically deleted or archived (DR-017, AC-2).
    /// </summary>
    ClinicalRecords,

    /// <summary>
    /// Completed and active appointment records (<c>Appointment</c> table, non-cancelled).
    /// Default retention: 3 years before archival (DR-018).
    /// Archival logic implemented in task_002.
    /// </summary>
    Appointments,

    /// <summary>
    /// Notification delivery log entries (<c>NotificationLog</c> table).
    /// Purged after 90 days via nightly batch job (DR-019, AC-4).
    /// </summary>
    Notifications,

    /// <summary>
    /// Cancelled appointment records.
    /// Default retention: 1 year before archival (DR-020).
    /// Archival logic implemented in task_002.
    /// </summary>
    CancelledAppointments,
}
