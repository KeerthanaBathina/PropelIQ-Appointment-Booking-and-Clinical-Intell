namespace UPACIP.DataAccess.Enums;

/// <summary>
/// Represents the lifecycle state of a detected clinical conflict (US_044, FR-053).
///
/// Conflicts progress through this state machine:
///   Detected → UnderReview → Resolved | Dismissed
///
/// Persisted as a VARCHAR column for forward-compatible enum extension.
/// </summary>
public enum ConflictStatus
{
    /// <summary>
    /// The AI service has flagged the conflict but no staff member has started review.
    /// Conflicts in this state appear in the active review queue.
    /// </summary>
    Detected,

    /// <summary>
    /// A staff member has opened the conflict for review but has not yet resolved or
    /// dismissed it.  Prevents double-assignment in concurrent review scenarios.
    /// </summary>
    UnderReview,

    /// <summary>
    /// Staff has reviewed the conflict, confirmed or corrected the underlying data,
    /// and closed the item.  <c>resolution_notes</c> and <c>resolved_at</c> are populated.
    /// </summary>
    Resolved,

    /// <summary>
    /// Staff determined the flagged conflict is a false positive or not clinically
    /// significant.  The item is removed from the active queue and archived.
    /// </summary>
    Dismissed,
}
