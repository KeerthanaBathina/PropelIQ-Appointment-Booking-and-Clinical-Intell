namespace UPACIP.Service.Backup.Models;

/// <summary>
/// Per-table row count comparison between the production source and the restored test database (AC-4).
/// </summary>
public sealed record TableRowCountComparison
{
    public required string SchemaName   { get; init; }
    public required string TableName    { get; init; }
    public required long   SourceCount  { get; init; }
    public required long   RestoredCount { get; init; }
    public required bool   Matched      { get; init; }
}

/// <summary>
/// Per-table deterministic checksum comparison between the production source and the restored test database (AC-4).
/// </summary>
public sealed record TableChecksumComparison
{
    public required string  SchemaName       { get; init; }
    public required string  TableName        { get; init; }
    public required string? SourceChecksum   { get; init; }
    public required string? RestoredChecksum { get; init; }
    public required bool    Matched          { get; init; }
}

/// <summary>
/// Complete result of a quarterly backup restoration test (AC-3, AC-4, DR-026).
/// Returned by <c>IBackupRestorationTestService.RunRestorationTestAsync</c> and
/// exposed via the admin API endpoint.
/// </summary>
public sealed record RestorationTestResult
{
    /// <summary>True only when all three validation checks (row counts, FK integrity, checksums) pass.</summary>
    public required bool OverallSuccess { get; init; }

    /// <summary>Name of the backup file that was restored.</summary>
    public required string BackupFileName { get; init; }

    /// <summary>UTC timestamp when the restoration was performed.</summary>
    public required DateTime RestoredAtUtc { get; init; }

    /// <summary>Elapsed time for the <c>pg_restore</c> operation only (excludes decrypt + validation).</summary>
    public required TimeSpan RestorationDuration { get; init; }

    /// <summary>Per-table row count comparison between production and restored databases (AC-4).</summary>
    public required List<TableRowCountComparison> RowCountResults { get; init; }

    /// <summary>True when all table row counts match exactly.</summary>
    public required bool RowCountsPassed { get; init; }

    /// <summary>True when no unvalidated or violated FK constraints are detected in the restored database (AC-4).</summary>
    public required bool ReferentialIntegrityPassed { get; init; }

    /// <summary>Details of any FK integrity violations found; empty on success.</summary>
    public required List<string> ReferentialIntegrityErrors { get; init; }

    /// <summary>True when all table-level checksums match between source and restored (AC-4).</summary>
    public required bool ChecksumPassed { get; init; }

    /// <summary>Per-table checksum comparison between production and restored databases (AC-4).</summary>
    public required List<TableChecksumComparison> ChecksumResults { get; init; }

    /// <summary>Restoration-level error message; null when pg_restore succeeded.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Quarterly test scheduling status returned by <c>GetQuarterlyTestStatusAsync</c>.
/// </summary>
public sealed record QuarterlyTestStatus
{
    /// <summary>"Completed", "DueSoon", or "Overdue".</summary>
    public required string Status { get; init; }

    /// <summary>UTC start of the current quarter.</summary>
    public required DateTime QuarterStart { get; init; }

    /// <summary>UTC end of the current quarter (exclusive).</summary>
    public required DateTime QuarterEnd { get; init; }

    /// <summary>Date of the most recent test this quarter; null if no test has been run yet.</summary>
    public DateTime? LastTestDate { get; init; }

    /// <summary>Whether the most recent test this quarter passed all validations; null if no test run.</summary>
    public bool? LastTestPassed { get; init; }

    /// <summary>
    /// Number of days remaining until the end of the quarter.
    /// Negative when the quarter has ended without a test.
    /// </summary>
    public required int DaysRemainingInQuarter { get; init; }
}
