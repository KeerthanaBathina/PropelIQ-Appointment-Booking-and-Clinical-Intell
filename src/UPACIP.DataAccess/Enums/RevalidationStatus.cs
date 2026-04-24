namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Tracks the revalidation state of a <c>MedicalCode</c> against the current
/// ICD-10 code library after a quarterly refresh cycle (US_047 AC-3).
/// </summary>
public enum RevalidationStatus
{
    /// <summary>
    /// The code was validated against the current library version and is still
    /// active with no deprecation recorded.
    /// </summary>
    Valid,

    /// <summary>
    /// The code requires staff review — either a new library version is available
    /// and the code has not yet been revalidated, or a confidence threshold was
    /// not met during the last revalidation pass.
    /// </summary>
    PendingReview,

    /// <summary>
    /// The code was active when originally assigned but has since been deprecated
    /// in the library.  A <c>replacement_code</c> is available in <c>Icd10CodeLibrary</c>
    /// for the staff to apply (edge case: deprecated-code handling).
    /// </summary>
    DeprecatedReplaced,
}
