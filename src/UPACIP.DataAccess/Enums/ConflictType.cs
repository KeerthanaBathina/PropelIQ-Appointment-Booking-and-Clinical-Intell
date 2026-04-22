namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Classifies the type of clinical data conflict detected by the AI conflict detection service
/// (US_044, AC-2, AC-3, AC-5, FR-053).
///
/// Persisted as a VARCHAR column so new values can be added without a schema migration.
/// </summary>
public enum ConflictType
{
    /// <summary>
    /// Two or more clinical documents record conflicting medication information
    /// (e.g., different dosages, routes, or statuses for the same drug).
    /// Maps to AC-2 source citation requirement.
    /// </summary>
    MedicationDiscrepancy,

    /// <summary>
    /// The same diagnosis appears in two or more documents with differing dates,
    /// codes, or active/resolved status, indicating a potential data quality issue.
    /// Maps to AC-2 duplicate diagnosis detection.
    /// </summary>
    DuplicateDiagnosis,

    /// <summary>
    /// Clinical event timestamps fail chronological plausibility validation
    /// (e.g., a procedure recorded before its prerequisite diagnosis).
    /// Maps to AC-5 date conflict detection.
    /// </summary>
    DateInconsistency,

    /// <summary>
    /// A prescribed or recorded medication is contraindicated given another known
    /// medication, allergy, or patient condition — escalated as URGENT (AC-3).
    /// </summary>
    MedicationContraindication,
}
