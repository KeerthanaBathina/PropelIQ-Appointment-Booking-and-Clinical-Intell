namespace UPACIP.Service.Migration.Models;

// ─────────────────────────────────────────────────────────────────────────────
// Supporting records
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Per-table structure checksum comparison between pre- and post-migration state.
/// </summary>
public sealed record TableStructureCheck
{
    public required string  TableName      { get; init; }
    public string?          PreChecksum    { get; init; }
    public required string  PostChecksum   { get; init; }
    /// <summary>
    /// <c>true</c> when the table was intentionally targeted by the migration and
    /// a checksum change is therefore expected.
    /// </summary>
    public bool             ExpectedChange { get; init; }
    /// <summary>
    /// <c>true</c> when the checksum is unchanged OR when the change was expected.
    /// <c>false</c> signals an anomaly (untargeted table changed unexpectedly).
    /// </summary>
    public bool             Matched        { get; init; }
}

/// <summary>
/// Per-table row count comparison between pre- and post-migration state.
/// </summary>
public sealed record TableRowCountCheck
{
    public required string TableName      { get; init; }
    public long            PreCount       { get; init; }
    public long            PostCount      { get; init; }
    public long            Delta          { get; init; }
    /// <summary>
    /// <c>true</c> when the migration includes DML and row count changes are expected.
    /// </summary>
    public bool            ExpectedChange { get; init; }
}

// ─────────────────────────────────────────────────────────────────────────────
// Primary DTO
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Complete result of a post-migration integrity verification run (US_091 task_002, AC-5, DR-032).
/// </summary>
public sealed record VerificationResult
{
    /// <summary><c>true</c> if all verification checks passed without fatal errors.</summary>
    public bool OverallPassed { get; init; }

    /// <summary>Per-table structure checksum comparisons.</summary>
    public List<TableStructureCheck> StructureChecks { get; init; } = [];

    /// <summary>Per-table row count comparisons.</summary>
    public List<TableRowCountCheck> RowCountChecks { get; init; } = [];

    /// <summary><c>true</c> if all foreign key constraints are validated.</summary>
    public bool ForeignKeyConstraintsPassed { get; init; }

    /// <summary>Details of any FK validation failures.</summary>
    public List<string> ForeignKeyIssues { get; init; } = [];

    /// <summary><c>true</c> if migration checksums in <c>__EFMigrationsHistory</c> match recomputed values.</summary>
    public bool HistoryChecksumPassed { get; init; }

    /// <summary>Non-fatal anomalies detected during verification.</summary>
    public List<string> Warnings { get; init; } = [];

    /// <summary>Fatal integrity issues detected during verification.</summary>
    public List<string> Errors { get; init; } = [];

    /// <summary>UTC timestamp when verification was performed.</summary>
    public DateTime VerifiedAtUtc { get; init; }
}
