namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Tracks a HIPAA Right of Access data export request (US_094, NFR-044, AC-1).
/// Each request has a 30-day SLA deadline from submission to fulfillment.
/// </summary>
public sealed class DataAccessRequest
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to the patient whose data is being exported.</summary>
    public Guid PatientId { get; set; }

    /// <summary>"DataAccess" for this task; "DataDeletion" for task_002.</summary>
    public string RequestType { get; set; } = "DataAccess";

    /// <summary>Lifecycle status: Submitted → Processing → Completed | Failed.</summary>
    public string Status { get; set; } = "Submitted";

    /// <summary>UTC timestamp when the patient submitted the request.</summary>
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>30-day SLA deadline computed from <see cref="RequestedAtUtc"/> (NFR-044).</summary>
    public DateTime DeadlineUtc { get; set; }

    /// <summary>UTC timestamp when the export was generated. Null while pending or processing.</summary>
    public DateTime? CompletedAtUtc { get; set; }

    /// <summary>Relative path to the generated ZIP archive. Null until completed.</summary>
    public string? ExportFilePath { get; set; }

    /// <summary>Size of the generated ZIP archive in bytes. Null until completed.</summary>
    public long? ExportFileSizeBytes { get; set; }

    /// <summary>Error details when status is "Failed". Null otherwise.</summary>
    public string? FailureReason { get; set; }

    /// <summary>Email or identity of the patient or admin who submitted the request.</summary>
    public string RequestedBy { get; set; } = string.Empty;

    /// <summary>Identity of the admin who triggered processing. Null for auto-processed requests.</summary>
    public string? ProcessedBy { get; set; }

    /// <summary>UTC timestamp when the record was first persisted.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the last write to this record.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ─── Navigation ───────────────────────────────────────────────────────────

    /// <summary>Navigation property to the associated patient.</summary>
    public Patient? Patient { get; set; }
}
