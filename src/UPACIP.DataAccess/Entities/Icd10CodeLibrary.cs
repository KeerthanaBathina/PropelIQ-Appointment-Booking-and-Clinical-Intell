namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Reference table for ICD-10 code library entries used to validate AI-generated
/// diagnosis codes (DR-015, FR-063, US_047).
///
/// Each row represents one ICD-10 code as it exists in a specific library version.
/// When a quarterly refresh introduces a new library version, new rows are inserted;
/// deprecated codes receive a non-null <see cref="DeprecatedDate"/> and
/// <see cref="IsCurrent"/> is set to <c>false</c> by the update process (AC-3).
///
/// Design decisions:
/// <list type="bullet">
///   <item>
///     <strong>Versioned rows</strong>: each (<see cref="CodeValue"/>,
///     <see cref="LibraryVersion"/>) pair is unique so historical validation results
///     remain auditable without mutating existing rows.
///   </item>
///   <item>
///     <strong>No <c>BaseEntity</c> inheritance</strong>: this reference table uses
///     a dedicated <c>LibraryEntryId</c> primary key to distinguish it from
///     patient-record entities and avoid confusion with the common <c>Id</c> pattern.
///   </item>
/// </list>
/// </summary>
public sealed class Icd10CodeLibrary
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid LibraryEntryId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The ICD-10 alphanumeric code value (e.g. <c>"J18.9"</c>, <c>"E11.65"</c>).
    /// Max 10 characters — the longest valid ICD-10-CM code is 7 characters; 10 provides headroom.
    /// </summary>
    public string CodeValue { get; set; } = string.Empty;

    /// <summary>Full clinical description of the code (e.g. "Unspecified pneumonia").</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// ICD-10 chapter or block category (e.g. <c>"J00–J99"</c>, <c>"Respiratory System"</c>).
    /// Used for category-filtered RAG context building and display grouping.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>
    /// Date from which this code became effective in the ICD-10 standard (inclusive).
    /// Used for time-sensitive validation when the appointment date pre-dates a code addition.
    /// </summary>
    public DateOnly EffectiveDate { get; set; }

    /// <summary>
    /// Date from which this code was retired / deprecated in the ICD-10 standard.
    /// <c>null</c> for codes that are still active in their library version.
    /// When non-null, <see cref="IsCurrent"/> must be <c>false</c> and
    /// <see cref="ReplacementCode"/> should be populated if a direct successor exists.
    /// </summary>
    public DateOnly? DeprecatedDate { get; set; }

    /// <summary>
    /// The code that supersedes this entry when it is deprecated (e.g. a more specific
    /// code that replaces a general one after an ICD-10-CM update).
    /// <c>null</c> when no direct replacement exists or the code is still current.
    /// Max 10 characters (same bound as <see cref="CodeValue"/>).
    /// </summary>
    public string? ReplacementCode { get; set; }

    /// <summary>
    /// Semantic version of the ICD-10 library release this row belongs to (e.g. <c>"2026.1"</c>).
    /// Together with <see cref="CodeValue"/> forms the unique key for a versioned entry.
    /// Max 20 characters.
    /// </summary>
    public string LibraryVersion { get; set; } = string.Empty;

    /// <summary>
    /// <c>true</c> when this entry is active in the <em>latest</em> library version.
    /// Set to <c>false</c> during quarterly refresh when a code is deprecated or superseded.
    /// The composite index on (<see cref="CodeValue"/>, <see cref="IsCurrent"/>) makes
    /// active-code lookups highly efficient (DR-015).
    /// </summary>
    public bool IsCurrent { get; set; } = true;

    /// <summary>UTC timestamp when this library entry row was first inserted.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the last write to this row (e.g. IsCurrent toggled to false).</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
