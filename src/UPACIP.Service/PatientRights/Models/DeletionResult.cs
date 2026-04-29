namespace UPACIP.Service.PatientRights.Models;

/// <summary>
/// Post-deletion scan result confirming whether all patient data has been removed from
/// active tables, cache, and file storage (US_094, AC-4).
/// </summary>
public sealed class DeletionVerificationResult
{
    /// <summary>True when no patient data remains in any active location.</summary>
    public bool FullyDeleted { get; set; }

    /// <summary>List of active locations where patient data was still found (should be empty).</summary>
    public List<string> RemainingDataLocations { get; set; } = new();

    /// <summary>True when audit log entries are retained per DR-016 (7-year retention).</summary>
    public bool AuditLogsRetained { get; set; }

    /// <summary>Number of entity tables verified in the post-deletion scan.</summary>
    public int TablesVerified { get; set; }

    /// <summary>Number of Redis cache keys still matching the patient ID pattern (should be 0).</summary>
    public int CacheKeysRemaining { get; set; }

    /// <summary>Number of document files still on disk for this patient (should be 0).</summary>
    public int FilesRemaining { get; set; }
}

/// <summary>
/// Aggregated result of the six-phase patient data deletion pipeline (US_094, AC-3, AC-4).
/// </summary>
public sealed class DeletionResult
{
    /// <summary>The associated data access request ID.</summary>
    public Guid RequestId { get; set; }

    /// <summary>The patient whose data was deleted.</summary>
    public Guid PatientId { get; set; }

    /// <summary>Final pipeline status: "Completed", "CompletedWithWarnings", "Failed".</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Number of pending appointments cancelled in phase 1 (edge case 1).</summary>
    public int AppointmentsCancelled { get; set; }

    /// <summary>Number of shared data records anonymized in phase 2 (edge case 2).</summary>
    public int RecordsAnonymized { get; set; }

    /// <summary>Per-entity hard-deletion counts from phase 3 (e.g. {"Appointments": 5}).</summary>
    public Dictionary<string, int> EntitiesDeleted { get; set; } = new();

    /// <summary>True when the patient record was soft-deleted and PII anonymized in phase 4.</summary>
    public bool PatientSoftDeleted { get; set; }

    /// <summary>Number of Redis cache keys purged in phase 5.</summary>
    public int CacheKeysDeleted { get; set; }

    /// <summary>Number of clinical document files deleted from disk in phase 5.</summary>
    public int FilesDeleted { get; set; }

    /// <summary>True when audit log references were anonymized in phase 6.</summary>
    public bool AuditLogsAnonymized { get; set; }

    /// <summary>Post-deletion verification results confirming no data remains (AC-4).</summary>
    public DeletionVerificationResult Verification { get; set; } = new();

    /// <summary>Non-fatal issues encountered during deletion (e.g. locked files, unavailable cache).</summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>Total wall-clock time for the full pipeline.</summary>
    public TimeSpan Duration { get; set; }
}
