namespace UPACIP.Service.Import.Models;

/// <summary>
/// Configuration for the CSV import engine (US_092 task_001).
/// Bound from the <c>"CsvImport"</c> configuration section via <see cref="IOptionsMonitor{T}"/>.
/// </summary>
public sealed class ImportOptions
{
    public const string SectionName = "CsvImport";

    /// <summary>
    /// Number of valid rows persisted per <c>SaveChangesAsync</c> call.
    /// Smaller batches reduce memory pressure; larger batches improve throughput.
    /// Default: 1000.
    /// </summary>
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// Maximum CSV file size accepted. Files exceeding this limit are rejected
    /// immediately to prevent memory exhaustion. Default: 50 MB.
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 52_428_800; // 50 MB

    /// <summary>
    /// Maximum number of row-level validation errors before import is aborted early.
    /// Prevents unbounded error report growth on severely malformed files. Default: 1000.
    /// </summary>
    public int MaxErrorsBeforeAbort { get; set; } = 1000;

    /// <summary>
    /// Restricts which entity types can be targeted by the import endpoint.
    /// Must match the <c>EntityTypeName</c> value returned by each import profile.
    /// Default: Patient, Appointment, User.
    /// </summary>
    public List<string> AllowedEntityTypes { get; set; } = ["Patient", "Appointment", "User"];
}
