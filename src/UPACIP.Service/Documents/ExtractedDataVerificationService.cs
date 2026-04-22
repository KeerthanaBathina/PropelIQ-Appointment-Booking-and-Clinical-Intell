using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities.OwnedTypes;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Documents;

/// <summary>
/// Applies verification rules, data corrections, and per-row attribution stamping for
/// extracted clinical data (US_041 AC-4, EC-1, EC-2).
///
/// Design principles:
///   - Only rows with <c>FlaggedForReview = true</c> AND <c>VerificationStatus = Pending</c>
///     are eligible for single-row verify/correct.
///   - EC-1: <c>ReviewReason.ConfidenceUnavailable</c> rows remain review-gated until
///     explicit verification; they are never silently auto-accepted.
///   - Bulk verify skips already-verified rows (idempotent); only pending flagged rows updated.
///   - Each verification stamps <c>VerifiedByUserId</c>, <c>VerifiedAtUtc</c>, and
///     <c>VerificationStatus</c> in a single EF Core <c>SaveChangesAsync</c> call.
///   - PHI-rich <c>DataContent</c> is never emitted to application logs (OWASP A09).
/// </summary>
public sealed class ExtractedDataVerificationService : IExtractedDataVerificationService
{
    private readonly ApplicationDbContext                            _db;
    private readonly ILogger<ExtractedDataVerificationService>      _logger;

    public ExtractedDataVerificationService(
        ApplicationDbContext                         db,
        ILogger<ExtractedDataVerificationService>   logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ─── Single-row verify / correct ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<(VerifiedRowResult? Row, IReadOnlyDictionary<string, int> RemainingFlaggedCounts)> VerifySingleAsync(
        Guid               extractedDataId,
        string             action,
        Guid               verifierId,
        string             verifierName,
        CorrectionPayload? correctedContent,
        CancellationToken  cancellationToken)
    {
        var row = await _db.ExtractedData
            .Include(e => e.Document)
            .FirstOrDefaultAsync(e => e.Id == extractedDataId, cancellationToken);

        if (row is null)
        {
            _logger.LogWarning(
                "VerifySingleAsync: row not found. ExtractedDataId={ExtractedDataId}",
                extractedDataId);
            return (null, new Dictionary<string, int>());
        }

        // Row must be flagged for review and still pending — EC-1 applies to ConfidenceUnavailable.
        if (!row.FlaggedForReview || row.VerificationStatus != VerificationStatus.Pending)
        {
            _logger.LogWarning(
                "VerifySingleAsync: row ineligible for verification. " +
                "ExtractedDataId={ExtractedDataId} FlaggedForReview={Flagged} Status={Status}",
                extractedDataId, row.FlaggedForReview, row.VerificationStatus);
            return (null, new Dictionary<string, int>());
        }

        // Apply correction payload if present (only on corrected action).
        if (action == "corrected" && correctedContent is not null)
        {
            ApplyCorrection(row, correctedContent);
        }

        var nowUtc                = DateTime.UtcNow;
        row.VerifiedByUserId      = verifierId;
        row.VerifiedAtUtc         = nowUtc;
        row.VerificationStatus    = action == "corrected"
            ? VerificationStatus.Corrected
            : VerificationStatus.Verified;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "VerifySingleAsync: row verified. ExtractedDataId={ExtractedDataId} Action={Action} " +
            "VerifierId={VerifierId}",
            extractedDataId, action, verifierId);

        var remaining = await BuildFlaggedCountsAsync(
            new[] { row.DocumentId }, cancellationToken);

        return (
            new VerifiedRowResult
            {
                ExtractedDataId    = row.Id,
                VerificationStatus = row.VerificationStatus.ToString(),
                VerifiedAtUtc      = nowUtc,
                VerifiedByName     = verifierName,
            },
            remaining);
    }

    // ─── Bulk verify ──────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<(IReadOnlyList<VerifiedRowResult> VerifiedRows, int SkippedCount, IReadOnlyDictionary<string, int> RemainingFlaggedCounts)> BulkVerifyAsync(
        IReadOnlyList<Guid> extractedDataIds,
        Guid                verifierId,
        string              verifierName,
        CancellationToken   cancellationToken)
    {
        var rows = await _db.ExtractedData
            .Where(e => extractedDataIds.Contains(e.Id))
            .ToListAsync(cancellationToken);

        var eligible = rows
            .Where(r => r.FlaggedForReview && r.VerificationStatus == VerificationStatus.Pending)
            .ToList();

        int skipped = rows.Count - eligible.Count;

        if (eligible.Count == 0)
        {
            _logger.LogWarning(
                "BulkVerifyAsync: no eligible rows. Requested={Requested} Skipped={Skipped}",
                extractedDataIds.Count, skipped);
            return ([], skipped, new Dictionary<string, int>());
        }

        var nowUtc = DateTime.UtcNow;
        foreach (var row in eligible)
        {
            row.VerifiedByUserId   = verifierId;
            row.VerifiedAtUtc      = nowUtc;
            row.VerificationStatus = VerificationStatus.Verified;
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "BulkVerifyAsync: verified {Count} rows. Skipped={Skipped} VerifierId={VerifierId}",
            eligible.Count, skipped, verifierId);

        var documentIds = eligible.Select(r => r.DocumentId).Distinct().ToArray();
        var remaining   = await BuildFlaggedCountsAsync(documentIds, cancellationToken);

        var results = eligible
            .Select(r => new VerifiedRowResult
            {
                ExtractedDataId    = r.Id,
                VerificationStatus = r.VerificationStatus.ToString(),
                VerifiedAtUtc      = nowUtc,
                VerifiedByName     = verifierName,
            })
            .ToList();

        return (results, skipped, remaining);
    }

    // ─── Query methods ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<string, int>> GetFlaggedCountsAsync(
        IReadOnlyList<Guid> documentIds,
        CancellationToken   cancellationToken)
    {
        return await BuildFlaggedCountsAsync(documentIds, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ExtractedDataQueryRow>> GetByDocumentAsync(
        Guid              documentId,
        CancellationToken cancellationToken)
    {
        var rows = await _db.ExtractedData
            .Where(e => e.DocumentId == documentId)
            .Include(e => e.VerifiedByUser)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        return rows.Select(ProjectRow).ToList();
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ExtractedDataQueryRow>> GetByPatientAsync(
        Guid              patientId,
        CancellationToken cancellationToken)
    {
        var rows = await _db.ExtractedData
            .Where(e => e.Document.PatientId == patientId)
            .Include(e => e.VerifiedByUser)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        return rows.Select(ProjectRow).ToList();
    }

    // ─── Private helpers ───────────────────────────────────────────────────────────────────────

    private static void ApplyCorrection(
        DataAccess.Entities.ExtractedData row,
        CorrectionPayload                 payload)
    {
        row.DataContent ??= new ExtractedDataContent();

        if (payload.NormalizedValue is not null)
            row.DataContent.NormalizedValue = payload.NormalizedValue;
        if (payload.RawText is not null)
            row.DataContent.RawText = payload.RawText;
        if (payload.Unit is not null)
            row.DataContent.Unit = payload.Unit;
    }

    private async Task<IReadOnlyDictionary<string, int>> BuildFlaggedCountsAsync(
        IEnumerable<Guid> documentIds,
        CancellationToken cancellationToken)
    {
        var ids = documentIds.ToArray();

        var counts = await _db.ExtractedData
            .Where(e => ids.Contains(e.DocumentId)
                     && e.FlaggedForReview
                     && e.VerificationStatus == VerificationStatus.Pending)
            .GroupBy(e => e.DocumentId)
            .Select(g => new { DocumentId = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return counts.ToDictionary(
            x => x.DocumentId.ToString(),
            x => x.Count);
    }

    private static ExtractedDataQueryRow ProjectRow(DataAccess.Entities.ExtractedData e)
    {
        // Confidence score: treat 0.0f stored for confidence-unavailable rows as null for the UI.
        // ReviewReason.ConfidenceUnavailable is the discriminator (EC-1).
        float? confidenceScore = e.ReviewReason == ReviewReason.ConfidenceUnavailable
            ? null
            : e.ConfidenceScore;

        return new ExtractedDataQueryRow
        {
            ExtractedDataId    = e.Id,
            DocumentId         = e.DocumentId,
            DataType           = e.DataType.ToString(),
            DataContent        = e.DataContent,
            ConfidenceScore    = confidenceScore,
            FlaggedForReview   = e.FlaggedForReview,
            ReviewReason       = e.ReviewReason.ToString(),
            VerificationStatus = e.VerificationStatus.ToString(),
            VerifiedAtUtc      = e.VerifiedAtUtc,
            VerifiedByName     = e.VerifiedByUser?.FullName,
            PageNumber         = e.PageNumber,
            ExtractionRegion   = e.ExtractionRegion,
        };
    }
}
