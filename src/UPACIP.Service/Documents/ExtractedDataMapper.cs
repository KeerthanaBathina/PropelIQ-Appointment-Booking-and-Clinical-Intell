using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.AI.ClinicalExtraction;
using ReviewReasonEnum = UPACIP.DataAccess.Enums.ReviewReason;
using VerificationStatusEnum = UPACIP.DataAccess.Enums.VerificationStatus;

namespace UPACIP.Service.Documents;

/// <summary>
/// Maps a normalized <see cref="ClinicalExtractionResult"/> into <see cref="ExtractedData"/>
/// entity instances ready for EF Core insertion (US_040 AC-1–AC-5, US_041 AC-1, AC-2, EC-1, EC-2).
///
/// Responsibilities:
///   - Route each <see cref="ClinicalExtractedItem"/> to the correct <see cref="DataType"/> category.
///   - Build structured <c>SourceAttribution</c> strings encoding document linkage, page number,
///     and extraction region (AC-5; format: <c>page={n};region={region}</c>, max 200 chars).
///   - Propagate per-item <c>FlaggedForReview</c>, <c>ReviewReason</c>, and <c>VerificationStatus</c>
///     from the validated extraction item so every row carries complete review metadata at insert time
///     (US_041 AC-2, EC-1, EC-2).
///   - Leave <c>VerifiedByUserId</c> null — requires explicit staff sign-off post-extraction.
///
/// This class is deliberately pure (no database access, no logging side-effects).
/// All validation, PII sanitisation, and per-item confidence classification are performed
/// upstream in <see cref="ClinicalExtractionResultValidator"/> before this mapper is invoked.
/// </summary>
public static class ExtractedDataMapper
{
    // Maximum length of the SourceAttribution varchar(200) column (US_040 AC-5).
    private const int MaxAttributionLength = 200;

    // Confidence threshold reference — kept for guard-rail documentation alignment.
    // The actual per-item flagging decision is made upstream in ClinicalExtractionResultValidator
    // and propagated via ClinicalExtractedItem.FlaggedForReview (US_041 AC-2, EC-1).
    internal const double ConfidenceThreshold = ClinicalExtractionResultValidator.ConfidenceThreshold;

    /// <summary>
    /// Maps every item in <paramref name="result"/> into an <see cref="ExtractedData"/> entity.
    /// Returns an empty list when <see cref="ClinicalExtractionResult.Outcome"/> is not
    /// <see cref="ExtractionOutcome.Extracted"/> or when the item list is empty (EC-1, EC-2).
    /// </summary>
    /// <param name="documentId">Source document identifier (AC-5 linkage).</param>
    /// <param name="result">Normalised extraction envelope from the AI pipeline.</param>
    /// <returns>Ordered list of entities: medications, diagnoses, procedures, allergies.</returns>
    public static IReadOnlyList<ExtractedData> MapToEntities(
        Guid                    documentId,
        ClinicalExtractionResult result)
    {
        if (result.Outcome != ExtractionOutcome.Extracted || result.Items.Count == 0)
            return [];

        var entities = new List<ExtractedData>(result.Items.Count);

        foreach (var item in result.Items)
        {
            entities.Add(new ExtractedData
            {
                DocumentId         = documentId,
                DataType           = item.DataType,
                DataContent        = item.Content,
                ConfidenceScore    = (float)item.Confidence,
                PageNumber         = item.PageNumber,
                ExtractionRegion   = item.ExtractionRegion,
                SourceAttribution  = BuildAttribution(documentId, item.PageNumber, item.ExtractionRegion),
                FlaggedForReview   = item.FlaggedForReview,
                ReviewReason       = item.ReviewReason,
                VerificationStatus = VerificationStatusEnum.Pending,
                VerifiedByUserId   = null,
            });
        }

        return entities;
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the structured source-attribution string for <c>SourceAttribution</c> (AC-5).
    /// Format: <c>doc={documentId};page={page};region={region}</c> — truncated to 200 chars.
    /// </summary>
    private static string BuildAttribution(Guid documentId, int pageNumber, string extractionRegion)
    {
        var raw = $"doc={documentId};page={pageNumber};region={extractionRegion}";
        return raw.Length <= MaxAttributionLength
            ? raw
            : raw[..MaxAttributionLength];
    }

    /// <summary>
    /// Returns the count of entities with <see cref="DataType.Medication"/> in the list.
    /// </summary>
    public static int CountByType(IReadOnlyList<ExtractedData> entities, DataType type)
        => entities.Count(e => e.DataType == type);

    /// <summary>
    /// Returns the count of entities flagged for review in the list (US_041 EC-2).
    /// </summary>
    public static int CountFlaggedForReview(IReadOnlyList<ExtractedData> entities)
        => entities.Count(e => e.FlaggedForReview);

    /// <summary>
    /// Returns the count of entities with <see cref="ReviewReasonEnum.ConfidenceUnavailable"/> (US_041 EC-1).
    /// </summary>
    public static int CountConfidenceUnavailable(IReadOnlyList<ExtractedData> entities)
        => entities.Count(e => e.ReviewReason == ReviewReasonEnum.ConfidenceUnavailable);
}
