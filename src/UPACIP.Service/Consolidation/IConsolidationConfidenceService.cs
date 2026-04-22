namespace UPACIP.Service.Consolidation;

/// <summary>
/// Evaluates the aggregate AI confidence for consolidation results and identifies
/// extracted-data entries below the 80% auto-approve threshold (US_046 AC-1, FR-052).
///
/// Called after the consolidation pipeline completes to determine whether a patient's
/// profile data requires manual review. Entries below the threshold are returned
/// pre-formatted for the ManualReviewForm displayed by the frontend.
/// </summary>
public interface IConsolidationConfidenceService
{
    /// <summary>
    /// Returns all non-archived, unverified <c>ExtractedData</c> rows for a patient whose
    /// <c>ConfidenceScore</c> is below the 0.80 threshold, ordered by data type then score ascending.
    /// </summary>
    /// <param name="patientId">The patient whose extracted data is to be evaluated.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A <see cref="LowConfidenceResultDto"/> containing the items list and total count.
    /// Returns an empty result (not an error) when all entries meet the threshold.
    /// </returns>
    Task<LowConfidenceResultDto> GetLowConfidenceItemsAsync(Guid patientId, CancellationToken ct = default);

    /// <summary>
    /// Persists a batch of manually verified or corrected entries submitted by staff (AC-3).
    ///
    /// For each entry in <paramref name="request"/>:
    /// <list type="bullet">
    ///   <item>Sets <c>VerificationStatus = ManualVerified</c>.</item>
    ///   <item>When <c>CorrectedValue</c> is non-null, updates <c>DataContent.NormalizedValue</c>.</item>
    ///   <item>Stamps <c>VerifiedByUserId</c> and <c>VerifiedAtUtc</c>.</item>
    ///   <item>Creates an immutable <c>AuditLog</c> entry for each row (FR-093, NFR-012).</item>
    /// </list>
    ///
    /// All updates execute within a single database transaction (atomicity guarantee).
    ///
    /// Idempotency: the caller supplies an <paramref name="idempotencyKey"/>; this method checks
    /// a Redis cache entry and returns immediately if the key was already processed (NFR-034).
    /// </summary>
    /// <param name="patientId">Patient whose extracted-data rows are being verified.</param>
    /// <param name="request">Verification payload with entries and notes.</param>
    /// <param name="staffUserId">Staff user performing the verification (attribution, FR-093).</param>
    /// <param name="idempotencyKey">Optional deduplication key from the <c>Idempotency-Key</c> header.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// True when the verification was applied; false when the idempotency key was already processed
    /// (caller should return 200 without re-processing).
    /// </returns>
    Task<bool> ManualVerifyEntriesAsync(
        Guid                    patientId,
        ManualVerifyRequestDto  request,
        Guid                    staffUserId,
        string?                 idempotencyKey,
        CancellationToken       ct = default);
}
