namespace UPACIP.Service.AI.ConflictDetection;

/// <summary>
/// Severity classification for a detected clinical conflict (US_043, AIR-S09, AIR-S10).
///
/// Used by <see cref="ConflictDetectionService"/> to classify conflicts after LLM analysis
/// and determine which conflicts require urgent staff notification (AIR-S09).
///
/// Mapping to staff workflow:
///   Critical — Urgent staff notification triggered immediately (AIR-S09).
///   High     — Appears in the "needs review" queue for the next staff session.
///   Medium   — Informational flag; surfaced in the profile conflict panel.
///   Low      — Logged and included in the version snapshot; no active review required.
/// </summary>
public enum ConflictSeverity
{
    /// <summary>
    /// Medication contraindication — two or more active medications with a known drug–drug
    /// interaction risk.  Triggers an urgent staff notification (AIR-S09).
    /// </summary>
    Critical,

    /// <summary>
    /// Conflicting diagnoses — the same condition is recorded with contradictory details
    /// (e.g. conflicting ICD codes, directly contradictory status values).
    /// </summary>
    High,

    /// <summary>
    /// Date discrepancy — a clinical event has a chronologically implausible date
    /// (e.g. procedure recorded before birth date, or post-discharge prescription pre-dates admission).
    /// Also used for moderate diagnostic conflicts (AIR-S10).
    /// </summary>
    Medium,

    /// <summary>
    /// Near-duplicate or minor inconsistency — data points that differ in minor formatting
    /// or metadata but refer to the same underlying clinical fact.
    /// </summary>
    Low,
}
