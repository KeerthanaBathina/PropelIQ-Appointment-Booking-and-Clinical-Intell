namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Reference table for CPT procedure code library entries used to validate AI-generated
/// procedure codes (US_048, DR-015, FR-066, AC-4).
///
/// Each row represents one CPT code.  The <see cref="IsActive"/> flag differentiates
/// currently valid codes from those that have expired or been retired.
///
/// Design note: mirrors <see cref="Icd10CodeLibrary"/> but with CPT-specific column names:
/// <c>CptCode</c> (not <c>CodeValue</c>) and <c>ExpirationDate</c> (not <c>DeprecatedDate</c>).
/// The full EF Core configuration and database migration are created in task_003_db_cpt_code_library.
/// </summary>
public sealed class CptCodeLibrary
{
    /// <summary>Surrogate UUID primary key (column: code_id).</summary>
    public Guid CptCodeId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The AMA CPT alphanumeric code value (e.g. <c>"99213"</c>, <c>"80053"</c>).
    /// Max 10 characters.  Unique constraint enforced at DB level.
    /// </summary>
    public string CptCode { get; set; } = string.Empty;

    /// <summary>Full clinical description of the procedure (e.g. "Office Visit — Established, Level 3").</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// CPT category label (e.g. "Evaluation &amp; Management", "Surgery", "Radiology",
    /// "Pathology", "Medicine").  Max 50 characters.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Date from which this code became effective (inclusive).</summary>
    public DateOnly EffectiveDate { get; set; }

    /// <summary>
    /// Date from which this code expired / was retired.
    /// <c>null</c> for codes that are still active.
    /// When non-null, <see cref="IsActive"/> should be <c>false</c>.
    /// </summary>
    public DateOnly? ExpirationDate { get; set; }

    /// <summary>
    /// <c>true</c> while this code is current and billable; <c>false</c> for expired or
    /// superseded codes.  Quarterly refresh sets this to <c>false</c> for removed codes (AC-4).
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>UTC timestamp when this row was inserted.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp when this row was last updated.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
