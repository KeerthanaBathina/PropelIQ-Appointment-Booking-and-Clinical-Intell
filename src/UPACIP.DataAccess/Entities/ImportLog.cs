namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Persistent audit record for every CSV import run, including background jobs
/// (US_092 task_002, AC-2, AC-3).
///
/// One row is written per completed (or failed) import invocation, recording the
/// operator identity, file metadata, row-level counts, and a capped error report.
/// Full error reports exceeding <see cref="MaxInlineErrors"/> rows are written to
/// a side-car file referenced by <see cref="FullErrorReportPath"/>.
/// </summary>
public sealed class ImportLog
{
    public const int MaxInlineErrors = 100;

    public Guid     Id                  { get; set; } = Guid.NewGuid();

    /// <summary>Target entity type ("Patient", "Appointment", "User").</summary>
    public string   EntityType          { get; set; } = string.Empty;

    /// <summary>Original uploaded filename.</summary>
    public string   FileName            { get; set; } = string.Empty;

    /// <summary>Uploaded file size in bytes.</summary>
    public long     FileSizeBytes       { get; set; }

    /// <summary>Total data rows in the CSV (header excluded).</summary>
    public int      TotalRows           { get; set; }

    /// <summary>Rows successfully imported.</summary>
    public int      SuccessCount        { get; set; }

    /// <summary>Rows that failed validation.</summary>
    public int      ErrorCount          { get; set; }

    /// <summary>Rows skipped due to duplicate detection.</summary>
    public int      DuplicateCount      { get; set; }

    /// <summary>Overall import status ("Completed", "CompletedWithErrors", "Failed", "InProgress", "Queued").</summary>
    public string   Status              { get; set; } = string.Empty;

    /// <summary>
    /// JSON-serialized first <see cref="MaxInlineErrors"/> error entries.
    /// Null when there were no errors.
    /// </summary>
    public string?  ErrorReportJson     { get; set; }

    /// <summary>
    /// Absolute path to the full error report file when error count exceeds
    /// <see cref="MaxInlineErrors"/>. Null when the inline report covers all errors.
    /// </summary>
    public string?  FullErrorReportPath { get; set; }

    /// <summary>Admin user identity (sub claim or username) who triggered the import.</summary>
    public string   PerformedBy         { get; set; } = string.Empty;

    /// <summary>Total wall-clock duration of the import run (stored as seconds).</summary>
    public double   DurationSeconds     { get; set; }

    /// <summary>UTC timestamp when the import was initiated.</summary>
    public DateTime CreatedAtUtc        { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp when the import completed (null while in progress or queued).</summary>
    public DateTime? CompletedAtUtc     { get; set; }
}
