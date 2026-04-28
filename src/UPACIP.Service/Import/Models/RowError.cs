namespace UPACIP.Service.Import.Models;

/// <summary>
/// Details of a single row-level validation or persistence failure during CSV import
/// (US_092 task_001, AC-3).
///
/// Security (OWASP A03 / PII): sensitive fields (email, patient_id) are redacted in
/// <see cref="RawValue"/> — use <c>[REDACTED]</c> for email, patient identifiers, etc.
/// </summary>
public sealed record RowError
{
    /// <summary>1-based row number in the CSV file. Header row = 1; data starts at row 2.</summary>
    public int    RowNumber    { get; init; }

    /// <summary>Column name that triggered the error.</summary>
    public string ColumnName   { get; init; } = string.Empty;

    /// <summary>Human-readable description of what went wrong.</summary>
    public string ErrorMessage { get; init; } = string.Empty;

    /// <summary>
    /// Raw string value from the CSV cell, truncated to 100 characters.
    /// PII-sensitive column values (email, patient_id) are replaced with <c>[REDACTED]</c>.
    /// </summary>
    public string? RawValue    { get; init; }
}
