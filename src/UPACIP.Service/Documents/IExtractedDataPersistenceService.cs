using UPACIP.Service.AI.ClinicalExtraction;

namespace UPACIP.Service.Documents;

/// <summary>
/// Persists a normalized AI extraction envelope into the <c>extracted_data</c> table and
/// updates the source <c>ClinicalDocument</c> row to reflect the extraction outcome (US_040).
///
/// Implementations must:
/// - Write all <see cref="UPACIP.DataAccess.Entities.ExtractedData"/> rows within a single
///   EF Core transaction so partial extraction sets are never committed (AC-5 atomicity).
/// - Honour edge-case outcomes: skip row creation and flag for manual review when the
///   extraction envelope signals <c>no-data-extracted</c> or <c>unsupported-language</c> (EC-1, EC-2).
/// - Never expose PHI in log output.
/// </summary>
public interface IExtractedDataPersistenceService
{
    /// <summary>
    /// Persists extraction results for the given document and returns a normalised outcome summary.
    /// </summary>
    /// <param name="documentId">Identifier of the source <c>ClinicalDocument</c> row.</param>
    /// <param name="result">Normalised extraction envelope produced by the AI pipeline.</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    /// <returns>
    /// A <see cref="ClinicalExtractionOutcome"/> describing persisted counts and whether
    /// manual review was flagged on the document.
    /// </returns>
    Task<ClinicalExtractionOutcome> PersistAsync(
        Guid                    documentId,
        ClinicalExtractionResult result,
        CancellationToken       cancellationToken);
}
