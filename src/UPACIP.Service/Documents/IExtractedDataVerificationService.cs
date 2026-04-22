using UPACIP.DataAccess.Entities.OwnedTypes;

namespace UPACIP.Service.Documents;

/// <summary>
/// Orchestrates verification and correction of extracted clinical data rows (US_041 AC-4, EC-1, EC-2).
/// </summary>
public interface IExtractedDataVerificationService
{
    /// <summary>
    /// Verifies or corrects a single extracted data row, stamping the caller's identity and a UTC
    /// timestamp. Returns the updated row summary and refreshed flagged counts for the document.
    /// </summary>
    /// <param name="extractedDataId">Row to verify.</param>
    /// <param name="action">
    ///   <c>"verified"</c> — accept the existing value;
    ///   <c>"corrected"</c> — apply <paramref name="correctedContent"/> then accept.
    /// </param>
    /// <param name="verifierId">Identity of the staff member performing verification.</param>
    /// <param name="verifierName">Display name used for audit and response payloads.</param>
    /// <param name="correctedContent">Replacement content for correction flows; null for verify-only.</param>
    /// <param name="cancellationToken"/>
    /// <returns>
    ///   <c>(verifiedRow, remainingFlaggedCounts)</c> where <c>verifiedRow</c> is null when the
    ///   row does not exist or is ineligible.
    /// </returns>
    Task<(VerifiedRowResult? Row, IReadOnlyDictionary<string, int> RemainingFlaggedCounts)> VerifySingleAsync(
        Guid                        extractedDataId,
        string                      action,
        Guid                        verifierId,
        string                      verifierName,
        CorrectionPayload?          correctedContent,
        CancellationToken           cancellationToken);

    /// <summary>
    /// Bulk-verifies multiple extracted data rows in one database round-trip.
    /// Only rows with <c>FlaggedForReview = true</c> and <c>VerificationStatus = Pending</c> are
    /// updated; already-verified rows are counted in <c>skippedCount</c> (idempotent).
    /// </summary>
    /// <param name="extractedDataIds">Row identifiers to verify (max 100).</param>
    /// <param name="verifierId">Identity of the staff member performing the bulk verification.</param>
    /// <param name="verifierName">Display name used for audit and response payloads.</param>
    /// <param name="cancellationToken"/>
    /// <returns>
    ///   <c>(verifiedRows, skippedCount, remainingFlaggedCounts)</c>.
    /// </returns>
    Task<(IReadOnlyList<VerifiedRowResult> VerifiedRows, int SkippedCount, IReadOnlyDictionary<string, int> RemainingFlaggedCounts)> BulkVerifyAsync(
        IReadOnlyList<Guid>         extractedDataIds,
        Guid                        verifierId,
        string                      verifierName,
        CancellationToken           cancellationToken);

    /// <summary>
    /// Returns the remaining pending-review count per document for the given document IDs.
    /// Used by SCR-012 to refresh badge counts after verification actions.
    /// </summary>
    Task<IReadOnlyDictionary<string, int>> GetFlaggedCountsAsync(
        IReadOnlyList<Guid>         documentIds,
        CancellationToken           cancellationToken);

    /// <summary>
    /// Returns all extracted data rows for a document, projected for SCR-012 display.
    /// </summary>
    Task<IReadOnlyList<ExtractedDataQueryRow>> GetByDocumentAsync(
        Guid              documentId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns all extracted data rows for a patient across all their documents,
    /// projected for SCR-013 display.
    /// </summary>
    Task<IReadOnlyList<ExtractedDataQueryRow>> GetByPatientAsync(
        Guid              patientId,
        CancellationToken cancellationToken);
}

// ─── Value objects for service results ───────────────────────────────────────────────────────────

/// <summary>
/// Lightweight result record returned for each row touched by a verification operation.
/// </summary>
public sealed record VerifiedRowResult
{
    public Guid     ExtractedDataId    { get; init; }
    public string   VerificationStatus { get; init; } = string.Empty;
    public DateTime VerifiedAtUtc      { get; init; }
    public string   VerifiedByName     { get; init; } = string.Empty;
}

/// <summary>
/// Projection record used for GET query responses on extracted data.
/// </summary>
public sealed record ExtractedDataQueryRow
{
    public Guid                      ExtractedDataId    { get; init; }
    public Guid                      DocumentId         { get; init; }
    public string                    DataType           { get; init; } = string.Empty;
    public ExtractedDataContent?     DataContent        { get; init; }
    public float?                    ConfidenceScore    { get; init; }
    public bool                      FlaggedForReview   { get; init; }
    public string                    ReviewReason       { get; init; } = string.Empty;
    public string                    VerificationStatus { get; init; } = string.Empty;
    public DateTime?                 VerifiedAtUtc      { get; init; }
    public string?                   VerifiedByName     { get; init; }
    public int                       PageNumber         { get; init; }
    public string                    ExtractionRegion   { get; init; } = string.Empty;
}

/// <summary>
/// Replacement content fields supplied for correction flows.
/// Only the non-null properties are applied to the existing <c>ExtractedDataContent</c>.
/// </summary>
public sealed record CorrectionPayload
{
    public string? NormalizedValue { get; init; }
    public string? RawText         { get; init; }
    public string? Unit            { get; init; }
}
