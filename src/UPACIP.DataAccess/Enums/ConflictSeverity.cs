namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Indicates the clinical severity of a detected conflict (US_044, AC-3, FR-053).
///
/// The severity drives review-queue ordering and escalation behaviour:
/// <c>Critical</c> and <c>High</c> are surfaced to staff immediately; <c>Critical</c>
/// conflicts of type <see cref="ConflictType.MedicationContraindication"/> are additionally
/// marked urgent and moved to the top of the queue (AC-3).
///
/// Persisted as a VARCHAR column for forward-compatible enum extension.
/// </summary>
public enum ConflictSeverity
{
    /// <summary>
    /// Immediate patient-safety risk — e.g. confirmed contraindication that could cause
    /// serious harm.  Always triggers the URGENT escalation path (AC-3).
    /// </summary>
    Critical,

    /// <summary>
    /// Significant clinical discrepancy requiring prompt staff review but not an
    /// immediate safety emergency.
    /// </summary>
    High,

    /// <summary>
    /// Moderate inconsistency that should be reviewed in the next standard review cycle.
    /// </summary>
    Medium,

    /// <summary>
    /// Minor data quality issue that can be addressed in routine maintenance.
    /// </summary>
    Low,
}
