namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Persistent audit trail for each post-migration integrity verification run
/// (US_091 task_002, AC-5, DR-032).
///
/// One row is written per successful <c>MigrationExecutionService.ApplyPendingMigrationsAsync</c>
/// invocation that proceeds beyond dry-run. Records all four verification outcomes
/// (structure, row counts, FK constraints, history checksum) and serialized warning/error details.
/// </summary>
public sealed class MigrationVerificationLog
{
    public Guid     Id                    { get; set; }
    public string   MigrationName         { get; set; } = string.Empty;
    public bool     StructureCheckPassed  { get; set; }
    public bool     RowCountCheckPassed   { get; set; }
    public bool     ForeignKeyCheckPassed { get; set; }
    public bool     HistoryChecksumPassed { get; set; }
    public bool     OverallPassed         { get; set; }
    /// <summary>JSON-serialized list of non-fatal warning strings.</summary>
    public string?  WarningDetails        { get; set; }
    /// <summary>JSON-serialized list of fatal error strings.</summary>
    public string?  ErrorDetails          { get; set; }
    public DateTime VerifiedAtUtc         { get; set; }
}
