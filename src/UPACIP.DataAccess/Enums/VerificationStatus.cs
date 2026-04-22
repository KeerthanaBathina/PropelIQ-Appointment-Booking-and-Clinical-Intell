namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Tracks the review state of a single extracted clinical data row (US_041 AC-4, EC-2).
/// Persisted as a varchar column so new values can be added without a schema migration.
/// </summary>
public enum VerificationStatus
{
    /// <summary>Row has not yet been reviewed by staff; mandatory if <c>FlaggedForReview = true</c>.</summary>
    Pending,

    /// <summary>Staff accepted the extracted value as accurate — no data change.</summary>
    Verified,

    /// <summary>Staff corrected the extracted value before accepting it.</summary>
    Corrected,
}
