namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Tracks the staff verification lifecycle of a patient profile version (US_045, AC-4, FR-054).
///
/// A profile version progresses through this state machine as staff resolve detected conflicts:
///   Unverified → PartiallyVerified → Verified
///
/// Persisted as a VARCHAR column for forward-compatible enum extension.
/// </summary>
public enum ProfileVerificationStatus
{
    /// <summary>
    /// Default state for all newly created profile versions.
    /// One or more unresolved conflicts remain open against this version.
    /// </summary>
    Unverified,

    /// <summary>
    /// At least one conflict has been resolved but one or more conflicts remain open.
    /// Displayed in the staff dashboard to indicate review in progress.
    /// </summary>
    PartiallyVerified,

    /// <summary>
    /// All conflicts detected against this profile version have been resolved or dismissed
    /// by staff.  An audit log entry is created when this state is first entered (AC-4).
    /// </summary>
    Verified,
}
