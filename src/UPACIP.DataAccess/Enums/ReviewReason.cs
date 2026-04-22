namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Structured reason a clinical extraction row was placed in mandatory review (US_041 EC-1, EC-2).
/// Persisted as varchar to keep the schema extensible without DDL changes.
/// </summary>
public enum ReviewReason
{
    /// <summary>No review required — confidence is at or above the auto-approve threshold.</summary>
    None,

    /// <summary>Model confidence was below the 0.80 threshold (AC-2).</summary>
    LowConfidence,

    /// <summary>
    /// The AI pipeline could not assign any confidence score.
    /// Treated as review-required regardless of value (EC-1).
    /// </summary>
    ConfidenceUnavailable,
}
