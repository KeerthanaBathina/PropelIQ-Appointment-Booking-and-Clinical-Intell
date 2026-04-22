using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Auth;
using UPACIP.Service.Caching;

namespace UPACIP.Service.Consolidation;

/// <summary>
/// Evaluates AI confidence thresholds and persists manual verification entries (US_046 AC-1, AC-3).
///
/// Design decisions:
///   - Query scoped to non-archived, unverified rows only; previously-confirmed entries are excluded.
///   - Idempotency check uses Redis key <c>upacip:manual-verify:idempotency:{key}</c> with 24-h TTL.
///   - All DB writes for a batch execute in a single transaction to guarantee atomicity.
///   - Audit log is written inside the same transaction scope; failure does not silently skip audit (FR-093).
///   - No PII is written to structured logs (Serilog / NFR-019).
/// </summary>
public sealed class ConsolidationConfidenceService : IConsolidationConfidenceService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Auto-approve confidence threshold. Entries below this require manual review (AC-1).</summary>
    public const float ConfidenceThreshold = 0.80f;

    /// <summary>Metadata key for the extracted clinical record date.</summary>
    private const string RecordDateMetadataKey = "record_date";

    /// <summary>TTL for idempotency deduplication keys (NFR-034).</summary>
    private static readonly TimeSpan IdempotencyTtl = TimeSpan.FromHours(24);

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ApplicationDbContext                      _db;
    private readonly ICacheService                             _cache;
    private readonly IAuditLogService                          _audit;
    private readonly ILogger<ConsolidationConfidenceService>   _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public ConsolidationConfidenceService(
        ApplicationDbContext                    db,
        ICacheService                           cache,
        IAuditLogService                        audit,
        ILogger<ConsolidationConfidenceService> logger)
    {
        _db     = db;
        _cache  = cache;
        _audit  = audit;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IConsolidationConfidenceService — GetLowConfidenceItemsAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<LowConfidenceResultDto> GetLowConfidenceItemsAsync(Guid patientId, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "ConsolidationConfidenceService: querying low-confidence items. PatientId={PatientId}",
            patientId);

        var rows = await _db.ExtractedData
            .Include(ed => ed.Document)
            .Where(ed =>
                ed.Document.PatientId == patientId
                && ed.ConfidenceScore < ConfidenceThreshold
                && !ed.IsArchived
                && ed.VerificationStatus == VerificationStatus.Pending)
            .OrderBy(ed => ed.DataType)
            .ThenBy(ed => ed.ConfidenceScore)
            .ToListAsync(ct);

        var items = rows.Select(ed => new LowConfidenceItemDto
        {
            ExtractedDataId       = ed.Id,
            DataType              = ed.DataType,
            NormalizedValue       = ed.DataContent?.NormalizedValue,
            RawText               = ed.DataContent?.RawText,
            Unit                  = ed.DataContent?.Unit,
            ConfidenceScore       = ed.ConfidenceScore,
            SourceDocumentId      = ed.DocumentId,
            SourceDocumentName    = ed.Document.OriginalFileName,
            RecordDate            = ed.DataContent?.Metadata.GetValueOrDefault(RecordDateMetadataKey),
            IsIncompleteDate      = ed.IsIncompleteDate,
            DateConflictExplanation = ed.DateConflictExplanation,
        }).ToList();

        return new LowConfidenceResultDto { Items = items, TotalCount = items.Count };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IConsolidationConfidenceService — ManualVerifyEntriesAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<bool> ManualVerifyEntriesAsync(
        Guid                    patientId,
        ManualVerifyRequestDto  request,
        Guid                    staffUserId,
        string?                 idempotencyKey,
        CancellationToken       ct = default)
    {
        // ── Idempotency check ──────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var cacheKey   = $"upacip:manual-verify:idempotency:{idempotencyKey}";
            var alreadyDone = await _cache.GetAsync<string>(cacheKey, ct);
            if (alreadyDone is not null)
            {
                _logger.LogInformation(
                    "ConsolidationConfidenceService: idempotent re-submission detected. Key={Key}",
                    idempotencyKey);
                return false;
            }
        }

        if (request.Entries.Count == 0) return true;

        var entryIds = request.Entries.Select(e => e.ExtractedDataId).ToHashSet();

        // ── Load target rows — scoped to this patient for OWASP A01 (Broken Access Control) ──
        var rows = await _db.ExtractedData
            .Include(ed => ed.Document)
            .Where(ed =>
                entryIds.Contains(ed.Id)
                && ed.Document.PatientId == patientId
                && !ed.IsArchived)
            .ToListAsync(ct);

        if (rows.Count == 0)
        {
            _logger.LogWarning(
                "ConsolidationConfidenceService: no writable rows found for patient. PatientId={PatientId}",
                patientId);
            return true;
        }

        var rowMap = rows.ToDictionary(r => r.Id);
        var now    = DateTime.UtcNow;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        foreach (var entry in request.Entries)
        {
            if (!rowMap.TryGetValue(entry.ExtractedDataId, out var row)) continue;

            // Apply correction when a new value is provided; otherwise confirm as-is.
            if (!string.IsNullOrWhiteSpace(entry.CorrectedValue) && row.DataContent is not null)
            {
                row.DataContent.NormalizedValue = entry.CorrectedValue.Trim();
            }

            row.VerificationStatus = VerificationStatus.ManualVerified;
            row.VerifiedByUserId   = staffUserId;
            row.VerifiedAtUtc      = now;

            // Audit log — immutable append-only entry per verified row (FR-093, NFR-012).
            await _audit.LogAsync(
                action:       AuditAction.ManualDataVerified,
                userId:       staffUserId,
                resourceType: "ExtractedData",
                ipAddress:    string.Empty,
                userAgent:    string.Empty,
                resourceId:   row.Id,
                cancellationToken: ct);
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // ── Persist idempotency key ─────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var cacheKey = $"upacip:manual-verify:idempotency:{idempotencyKey}";
            await _cache.SetAsync(cacheKey, "done", IdempotencyTtl, ct);
        }

        _logger.LogInformation(
            "ConsolidationConfidenceService: manual verification applied. PatientId={PatientId}, Count={Count}",
            patientId, rows.Count);

        return true;
    }
}
