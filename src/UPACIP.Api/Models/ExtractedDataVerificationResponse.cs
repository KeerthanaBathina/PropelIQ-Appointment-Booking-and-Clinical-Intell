namespace UPACIP.Api.Models;

/// <summary>
/// Response returned after any verification operation (single or bulk) on extracted data (US_041).
///
/// Carries the updated state of each affected row together with the remaining flagged-item
/// count for the source document(s) so SCR-012 and SCR-013 can refresh without reloading
/// the full pipeline state.
/// </summary>
public sealed record ExtractedDataVerificationResponse
{
    /// <summary>Number of rows that were updated in this operation.</summary>
    public int VerifiedCount { get; init; }

    /// <summary>Number of rows already verified that were skipped (idempotent bulk operations).</summary>
    public int SkippedCount { get; init; }

    /// <summary>Updated row states returned for immediate UI refresh.</summary>
    public IReadOnlyList<VerifiedRowSummary> UpdatedRows { get; init; } = [];

    /// <summary>
    /// Remaining pending-review count per document ID (keyed by document GUID string).
    /// Lets SCR-012 refresh badge counts after a verification operation completes.
    /// </summary>
    public IReadOnlyDictionary<string, int> RemainingFlaggedCounts { get; init; } =
        new Dictionary<string, int>();
}

/// <summary>
/// Lightweight per-row summary returned inside <see cref="ExtractedDataVerificationResponse"/>.
/// Contains only the state fields SCR-013 needs to update the verification indicator.
/// </summary>
public sealed record VerifiedRowSummary
{
    /// <summary>Extracted data row identifier.</summary>
    public Guid ExtractedDataId { get; init; }

    /// <summary>Updated verification status after the operation.</summary>
    public string VerificationStatus { get; init; } = string.Empty;

    /// <summary>UTC timestamp when the verification was stamped.</summary>
    public DateTime? VerifiedAtUtc { get; init; }

    /// <summary>Display name of the staff member who performed the verification.</summary>
    public string VerifiedByName { get; init; } = string.Empty;
}
