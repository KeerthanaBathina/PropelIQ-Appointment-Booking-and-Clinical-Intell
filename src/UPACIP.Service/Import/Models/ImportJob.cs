namespace UPACIP.Service.Import.Models;

/// <summary>
/// In-memory state for a background CSV import job
/// (US_092 task_002, edge case 1 — files estimated at &gt;10,000 rows).
///
/// Stored in the singleton <c>ConcurrentDictionary&lt;Guid, ImportJob&gt;</c> so that both the
/// <c>CsvImportBackgroundService</c> (writer) and <c>ImportController</c> (reader) share live state.
/// </summary>
public sealed class ImportJob
{
    /// <summary>Unique identifier returned to the admin as the polling key.</summary>
    public Guid JobId { get; init; } = Guid.NewGuid();

    /// <summary>Target entity type name (e.g. "Patient", "Appointment", "User").</summary>
    public string EntityType { get; init; } = string.Empty;

    /// <summary>Temporary file path on disk. Deleted after processing.</summary>
    public string FilePath { get; init; } = string.Empty;

    /// <summary>Original uploaded filename (preserved for logging and history).</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Size of the uploaded file in bytes.</summary>
    public long FileSizeBytes { get; init; }

    /// <summary>Admin user identity who submitted the import.</summary>
    public string PerformedBy { get; init; } = string.Empty;

    /// <summary>Current lifecycle state of the job.</summary>
    public volatile string Status = "Queued";

    /// <summary>Live progress snapshot, updated after every batch flush (thread-safe write by background service).</summary>
    public volatile ImportProgress? Progress;

    /// <summary>Final result, set when the job transitions to Completed or Failed.</summary>
    public volatile ImportResult? Result;

    /// <summary>UTC timestamp when the job was enqueued.</summary>
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
}
