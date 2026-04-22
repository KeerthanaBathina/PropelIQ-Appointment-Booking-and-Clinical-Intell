using System.ComponentModel.DataAnnotations;

namespace UPACIP.Api.Models;

/// <summary>
/// Request body for bulk-verifying multiple flagged extracted data rows in one call (US_041 EC-2).
///
/// Each row in <see cref="ExtractedDataIds"/> is stamped with the calling staff member's
/// identity and a shared UTC verification timestamp. Only <c>FlaggedForReview = true</c>
/// rows with <c>VerificationStatus = Pending</c> are eligible; already-verified rows are
/// silently skipped to keep the operation idempotent.
///
/// Bulk verify does not support data corrections — use the single-row endpoint for that.
/// </summary>
public sealed record BulkVerifyExtractedDataRequest
{
    /// <summary>
    /// One or more extracted-data row identifiers to verify in a single operation.
    /// Maximum 100 per request to prevent unbounded bulk operations.
    /// </summary>
    [Required]
    [MinLength(1, ErrorMessage = "At least one extracted data ID is required.")]
    [MaxLength(100, ErrorMessage = "At most 100 extracted data IDs may be verified per request.")]
    public IReadOnlyList<Guid> ExtractedDataIds { get; init; } = [];
}
