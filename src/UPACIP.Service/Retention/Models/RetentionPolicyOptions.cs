using Microsoft.Extensions.Logging;

namespace UPACIP.Service.Retention.Models;

/// <summary>
/// Strongly-typed configuration for the data retention policy engine (US_086, DR-016–DR-020).
///
/// Loaded via <c>IOptionsMonitor&lt;RetentionPolicyOptions&gt;</c> so that configuration
/// changes in <c>appsettings.json</c> take effect on the next scheduled job cycle without
/// restarting the application (edge case 1: retroactive policy change applies from the
/// next nightly run; previously purged data cannot be recovered).
/// </summary>
public sealed class RetentionPolicyOptions
{
    /// <summary>Configuration section key in appsettings.json.</summary>
    public const string SectionName = "RetentionPolicy";

    // ── HIPAA minimum (DR-016) ─────────────────────────────────────────────
    private int _auditLogRetentionYears = 7;

    /// <summary>
    /// Minimum years audit logs must be retained (DR-016, HIPAA 45 CFR § 164.530(j)).
    /// Values below 7 are silently clamped to 7 — HIPAA mandates a 7-year minimum.
    /// Default: 7.
    /// </summary>
    public int AuditLogRetentionYears
    {
        get => _auditLogRetentionYears;
        set => _auditLogRetentionYears = value < 7 ? 7 : value;
    }

    /// <summary>
    /// When <c>true</c>, clinical records (ClinicalDocument, ExtractedData, MedicalCode)
    /// are never automatically deleted or archived (DR-017, AC-2).
    /// Default: true.
    /// </summary>
    public bool ClinicalRecordsIndefiniteRetention { get; set; } = true;

    /// <summary>
    /// Years before active (non-cancelled) appointments are eligible for archival (DR-018).
    /// Used by task_002 archival logic.
    /// Default: 3.
    /// </summary>
    public int AppointmentRetentionYears { get; set; } = 3;

    /// <summary>
    /// Days before notification log entries are eligible for purge by the nightly job (DR-019, AC-4).
    /// Default: 90.
    /// </summary>
    public int NotificationLogRetentionDays { get; set; } = 90;

    /// <summary>
    /// Years before cancelled appointments are eligible for archival (DR-020).
    /// Used by task_002 archival logic.
    /// Default: 1.
    /// </summary>
    public int CancelledAppointmentRetentionYears { get; set; } = 1;

    /// <summary>
    /// Local-time string (HH:mm) at which the nightly purge job executes.
    /// The host's local timezone is used for conversion to UTC.
    /// Default: "03:00" (3:00 AM local time).
    /// </summary>
    public string NightlyJobScheduleLocal { get; set; } = "03:00";

    /// <summary>
    /// Maximum records deleted per EF Core bulk-delete batch.
    /// Limits transaction duration to avoid long-running locks on the notifications table.
    /// Default: 1000.
    /// </summary>
    public int PurgeBatchSize { get; set; } = 1000;

    /// <summary>
    /// When <c>true</c>, entities referenced by audit log entries within the 7-year
    /// audit retention window are protected from deletion or archival regardless of
    /// their own category policy (edge case 2).
    /// Default: true.
    /// </summary>
    public bool EnforceAuditLogReferenceProtection { get; set; } = true;

    /// <summary>
    /// Days a soft-deleted patient record remains in the main schema before being
    /// moved to the archive schema (US_087 AC-4, DR-021).
    ///
    /// This window gives administrators time to restore accidentally soft-deleted
    /// patients via <c>IPatientSoftDeleteService.RestoreAsync</c>.  After the threshold
    /// elapses, the patient and all dependent data are archived by
    /// <c>PatientArchivalService</c> in the nightly batch (step 4).
    ///
    /// Default: 365 (one year after soft deletion).
    /// </summary>
    public int SoftDeletedPatientArchivalDays { get; set; } = 365;
}
