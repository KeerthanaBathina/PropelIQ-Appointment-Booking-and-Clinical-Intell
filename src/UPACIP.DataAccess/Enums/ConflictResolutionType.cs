namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Describes how a clinical conflict was resolved by a staff member (US_045, AC-2, EC-2).
///
/// Persisted as a VARCHAR column for forward-compatible enum extension.
/// </summary>
public enum ConflictResolutionType
{
    /// <summary>
    /// Staff reviewed both source values and selected one as the authoritative correct value.
    /// The chosen value is persisted to the consolidated profile via
    /// <see cref="ClinicalConflict.SelectedExtractedDataId"/> (AC-2).
    /// </summary>
    SelectedValue,

    /// <summary>
    /// Staff confirmed that both conflicting values are clinically valid and represent
    /// distinct events on different dates (Edge Case — "Both Valid — Different Dates").
    /// Both entries are preserved in the consolidated profile with distinct date attribution.
    /// </summary>
    BothValid,

    /// <summary>
    /// Staff determined that the detected conflict was a false positive produced by the AI
    /// detection pipeline and does not represent a genuine clinical inconsistency.
    /// No change is made to the consolidated profile.
    /// </summary>
    Dismissed,
}
