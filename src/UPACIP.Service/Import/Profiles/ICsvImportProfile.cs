using UPACIP.DataAccess;
using UPACIP.Service.Import.Models;

namespace UPACIP.Service.Import.Profiles;

/// <summary>
/// Defines column-to-property mapping, row-level validation, and duplicate detection
/// for a specific entity type in the CSV import pipeline (US_092 task_001, AC-1, AC-4).
/// </summary>
public interface ICsvImportProfile<T> where T : class
{
    /// <summary>
    /// The entity type name used to match against
    /// <see cref="ImportOptions.AllowedEntityTypes"/> (e.g. "Patient").
    /// </summary>
    string EntityTypeName { get; }

    /// <summary>
    /// Columns that must be present in the CSV header and contain a non-empty value
    /// for each row. Missing required columns cause a <c>HeaderValidationFailed</c> result.
    /// Column names should be lowercase (header names are normalised before comparison).
    /// </summary>
    string[] RequiredColumns { get; }

    /// <summary>
    /// Columns that may be present but are not required.
    /// The engine does not fail on missing optional columns.
    /// </summary>
    string[] OptionalColumns { get; }

    /// <summary>
    /// Set of column names whose <c>RawValue</c> in <see cref="RowError"/> should be
    /// replaced with <c>[REDACTED]</c> to prevent PII leakage in error reports.
    /// </summary>
    HashSet<string> PiiColumns { get; }

    /// <summary>
    /// Converts the raw string dictionary from one CSV row into a typed entity instance.
    /// Called only after <see cref="ValidateRow"/> returns no errors.
    /// </summary>
    T MapRow(Dictionary<string, string> row);

    /// <summary>
    /// Performs field-level validation on a raw row.
    /// Returns an empty list when the row is valid; returns one or more <see cref="RowError"/>
    /// instances describing each validation failure otherwise (AC-1, AC-3).
    /// </summary>
    List<RowError> ValidateRow(Dictionary<string, string> row, int rowNumber);

    /// <summary>
    /// Checks the database for an existing record that would violate a unique constraint
    /// if <paramref name="entity"/> were inserted.
    /// Returns <c>true</c> when a duplicate is found — the row should be skipped (AC-4).
    /// </summary>
    Task<bool> IsDuplicateAsync(
        T                   entity,
        ApplicationDbContext context,
        CancellationToken   ct = default);

    /// <summary>
    /// Produces a log-safe, PII-redacted string identifying a duplicate row for
    /// inclusion in <see cref="ImportResult.SkippedDuplicates"/> (AC-4).
    /// </summary>
    string DuplicateKey(T entity);
}
