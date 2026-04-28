namespace UPACIP.Service.Import.Models;

// ─────────────────────────────────────────────────────────────────────────────
// Status enum
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Overall outcome of a CSV import run.</summary>
public enum ImportStatus
{
    /// <summary>All rows processed without errors or early abort.</summary>
    Completed,
    /// <summary>Processing completed but one or more rows had validation errors.</summary>
    CompletedWithErrors,
    /// <summary>The CSV header row is missing one or more required columns.</summary>
    HeaderValidationFailed,
    /// <summary>
    /// Processing was stopped early because the error count exceeded
    /// <see cref="ImportOptions.MaxErrorsBeforeAbort"/>.
    /// </summary>
    Aborted,
}

// ─────────────────────────────────────────────────────────────────────────────
// Primary DTO
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Aggregate result of a CSV import run (US_092 task_001, AC-2, AC-3, AC-4).
/// </summary>
public sealed record ImportResult
{
    /// <summary>Overall outcome classification.</summary>
    public ImportStatus Status { get; init; }

    /// <summary>Which entity type was imported (e.g. "Patient", "Appointment", "User").</summary>
    public string EntityType { get; init; } = string.Empty;

    /// <summary>Total data rows in the CSV file (header excluded).</summary>
    public int TotalRows { get; init; }

    /// <summary>Number of rows that were successfully persisted to the database (AC-2).</summary>
    public int SuccessCount { get; init; }

    /// <summary>Number of rows rejected due to validation errors (AC-3).</summary>
    public int ErrorCount { get; init; }

    /// <summary>Number of rows skipped because they would duplicate an existing record (AC-4).</summary>
    public int DuplicateCount { get; init; }

    /// <summary>Detailed per-row, per-field error report (AC-3).</summary>
    public List<RowError> Errors { get; init; } = [];

    /// <summary>
    /// Summary identifiers of rows skipped by duplicate detection.
    /// Contains PII-redacted values — e.g. "patient:email=[REDACTED]" (AC-4).
    /// </summary>
    public List<string> SkippedDuplicates { get; init; } = [];

    /// <summary>Total wall-clock time for the import run.</summary>
    public TimeSpan Duration { get; init; }
}
