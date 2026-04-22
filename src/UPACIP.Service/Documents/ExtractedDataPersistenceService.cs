using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.AI.ClinicalExtraction;

namespace UPACIP.Service.Documents;

/// <summary>
/// Converts a normalized AI extraction envelope into persisted <c>ExtractedData</c> rows and
/// updates the source <c>ClinicalDocument</c> outcome — all within a single EF Core transaction
/// to prevent partial extraction sets (US_040 AC-1–AC-5, EC-1, EC-2).
///
/// Workflow per call:
///   1. Load the <see cref="UPACIP.DataAccess.Entities.ClinicalDocument"/> row.
///   2. Branch on <see cref="ExtractionOutcome"/>:
///      a. <c>Extracted</c> with items  → map rows + bulk-insert + save.
///      b. <c>NoDataExtracted</c>       → flag document for manual review (EC-1); no row insert.
///      c. <c>UnsupportedLanguage</c>   → flag document for manual review (EC-2); no row insert.
///      d. <c>InvalidResponse</c>       → log only; document already <c>Completed</c> from parsing.
///   3. All document status mutations and row inserts are committed in a single
///      <see cref="Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction"/> (AC-5).
///   4. Return a <see cref="ClinicalExtractionOutcome"/> with per-type counts and review flags.
/// </summary>
public sealed class ExtractedDataPersistenceService : IExtractedDataPersistenceService
{
    // Extraction outcome string constants written to ClinicalDocument.ExtractionOutcome (US_040 EC-1, EC-2).
    internal const string OutcomeExtracted           = "extracted";
    internal const string OutcomeNoDataExtracted     = "no-data-extracted";
    internal const string OutcomeUnsupportedLanguage = "unsupported-language";
    internal const string OutcomeInvalidResponse     = "invalid-response";

    private readonly ApplicationDbContext                       _db;
    private readonly IDocumentReplacementService               _replacementService;
    private readonly ILogger<ExtractedDataPersistenceService>  _logger;

    public ExtractedDataPersistenceService(
        ApplicationDbContext                      db,
        IDocumentReplacementService              replacementService,
        ILogger<ExtractedDataPersistenceService>  logger)
    {
        _db                 = db;
        _replacementService = replacementService;
        _logger             = logger;
    }

    /// <inheritdoc />
    public async Task<ClinicalExtractionOutcome> PersistAsync(
        Guid                     documentId,
        ClinicalExtractionResult result,
        CancellationToken        cancellationToken)
    {
        // ── Load source document ─────────────────────────────────────────────────
        var document = await _db.ClinicalDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        if (document is null)
        {
            _logger.LogError(
                "ExtractedDataPersistenceService: document not found. DocumentId={DocumentId}",
                documentId);

            return new ClinicalExtractionOutcome
            {
                Outcome            = result.Outcome,
                RequiresManualReview = false,
            };
        }

        // ── Branch on outcome ────────────────────────────────────────────────────
        return result.Outcome switch
        {
            ExtractionOutcome.Extracted        => await PersistExtractedAsync(documentId, document, result, cancellationToken),
            ExtractionOutcome.NoDataExtracted  => await FlagForManualReviewAsync(documentId, document, result, cancellationToken),
            ExtractionOutcome.UnsupportedLanguage => await FlagForManualReviewAsync(documentId, document, result, cancellationToken),
            _                                  => HandleInvalidResponse(documentId, result),
        };
    }

    // ─── Extracted outcome ────────────────────────────────────────────────────────

    private async Task<ClinicalExtractionOutcome> PersistExtractedAsync(
        Guid                     documentId,
        UPACIP.DataAccess.Entities.ClinicalDocument document,
        ClinicalExtractionResult result,
        CancellationToken        ct)
    {
        var entities = ExtractedDataMapper.MapToEntities(documentId, result);

        if (entities.Count == 0)
        {
            _logger.LogWarning(
                "ExtractedDataPersistenceService: extraction outcome was Extracted but mapper returned " +
                "zero entities — treating as no-data-extracted. DocumentId={DocumentId}",
                documentId);

            return await FlagForManualReviewAsync(
                documentId,
                document,
                new ClinicalExtractionResult
                {
                    Outcome       = ExtractionOutcome.NoDataExtracted,
                    Confidence    = result.Confidence,
                    OutcomeReason = "Mapper produced zero entities.",
                    Items         = [],
                },
                ct);
        }

        // Atomic: row inserts + document mutation in a single transaction (AC-5).
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            _db.ExtractedData.AddRange(entities);
            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        // Update document extraction outcome outside the row-insert transaction
        // so a secondary save failure doesn't roll back the already-committed rows.
        document.ExtractionOutcome = OutcomeExtracted;
        document.UpdatedAt         = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        var medCount  = ExtractedDataMapper.CountByType(entities, DataType.Medication);
        var dxCount   = ExtractedDataMapper.CountByType(entities, DataType.Diagnosis);
        var procCount = ExtractedDataMapper.CountByType(entities, DataType.Procedure);
        var algCount  = ExtractedDataMapper.CountByType(entities, DataType.Allergy);
        var lowConfidence          = ExtractedDataMapper.CountFlaggedForReview(entities)
                                     - ExtractedDataMapper.CountConfidenceUnavailable(entities);
        var confidenceUnavailable  = ExtractedDataMapper.CountConfidenceUnavailable(entities);

        _logger.LogInformation(
            "ExtractedDataPersistenceService: persisted extraction rows. " +
            "DocumentId={DocumentId} Meds={Meds} Dx={Dx} Proc={Proc} Allergy={Allergy} Total={Total} " +
            "LowConfidence={Low} ConfidenceUnavailable={Unavail}",
            documentId, medCount, dxCount, procCount, algCount, entities.Count,
            lowConfidence, confidenceUnavailable);

        // ── AC-3: Activate replacement version if this document supersedes another ──
        // Only triggers for replacement documents (PreviousVersionId set).
        // The activation step marks the old version Superseded, archives its extracted rows,
        // and sets ReconsolidationNeeded on this document (EC-2).
        if (document.PreviousVersionId.HasValue)
        {
            try
            {
                await _replacementService.ActivateReplacementAsync(documentId, ct);
            }
            catch (Exception ex)
            {
                // Log but do not fail the persistence outcome — extracted rows are committed.
                // The reconsolidation signal can be retried by a future EP-007 sweep.
                _logger.LogError(ex,
                    "ExtractedDataPersistenceService: replacement activation failed after extraction. " +
                    "DocumentId={DocumentId} PreviousVersionId={PrevId}",
                    documentId, document.PreviousVersionId);
            }
        }

        return new ClinicalExtractionOutcome
        {
            Outcome                    = ExtractionOutcome.Extracted,
            MedicationCount            = medCount,
            DiagnosisCount             = dxCount,
            ProcedureCount             = procCount,
            AllergyCount               = algCount,
            RequiresManualReview       = false,
            LowConfidenceCount         = Math.Max(0, lowConfidence),
            ConfidenceUnavailableCount = confidenceUnavailable,
        };
    }

    // ─── No-data / unsupported-language outcome ───────────────────────────────────

    private async Task<ClinicalExtractionOutcome> FlagForManualReviewAsync(
        Guid                     documentId,
        UPACIP.DataAccess.Entities.ClinicalDocument document,
        ClinicalExtractionResult result,
        CancellationToken        ct)
    {
        var reason = result.OutcomeReason
            ?? (result.Outcome == ExtractionOutcome.UnsupportedLanguage
                ? "Document language is not supported in Phase 1 (English only)."
                : "No structured clinical data was found in the document.");

        // Truncate to ManualReviewReason column constraint (varchar 500, US_039 task_004).
        const int MaxReasonLength = 500;
        if (reason.Length > MaxReasonLength)
            reason = reason[..MaxReasonLength];

        document.RequiresManualReview = true;
        document.ManualReviewReason   = reason;
        document.ExtractionOutcome    = result.Outcome == ExtractionOutcome.UnsupportedLanguage
            ? OutcomeUnsupportedLanguage
            : OutcomeNoDataExtracted;
        document.UpdatedAt            = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "ExtractedDataPersistenceService: document flagged for manual review. " +
            "DocumentId={DocumentId} Outcome={Outcome}",
            documentId, result.Outcome);

        return new ClinicalExtractionOutcome
        {
            Outcome              = result.Outcome,
            RequiresManualReview = true,
            ManualReviewReason   = reason,
        };
    }

    // ─── Invalid-response outcome ─────────────────────────────────────────────────

    private ClinicalExtractionOutcome HandleInvalidResponse(
        Guid                     documentId,
        ClinicalExtractionResult result)
    {
        _logger.LogError(
            "ExtractedDataPersistenceService: AI returned invalid response; no rows persisted. " +
            "DocumentId={DocumentId} Reason={Reason}",
            documentId, result.OutcomeReason ?? "unknown");

        // Note: document.ExtractionOutcome is not set here to avoid an extra DB load;
        // the document is already Completed from parsing and ExtractionOutcome remains null
        // until a retry or manual re-extraction.
        return new ClinicalExtractionOutcome
        {
            Outcome              = ExtractionOutcome.InvalidResponse,
            RequiresManualReview = false,
        };
    }
}
