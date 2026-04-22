namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Classifies the type of patient profile consolidation event (US_043, AC-2, FR-056).
///
/// Persisted as a VARCHAR column so new values can be added without a schema migration.
/// </summary>
public enum ConsolidationType
{
    /// <summary>
    /// First-time consolidation for the patient — no prior profile version exists.
    /// Creates the baseline consolidated profile from the initial set of clinical documents.
    /// </summary>
    Initial,

    /// <summary>
    /// Incremental update — new or re-processed clinical documents are merged into an
    /// existing consolidated profile.  A diff snapshot of changed fields is stored in
    /// <c>data_snapshot</c> for audit and resolution workflows.
    /// </summary>
    Incremental,
}
