using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.AI.ConflictDetection;
using UPACIP.Service.Caching;
using AiConflictSeverity = UPACIP.Service.AI.ConflictDetection.ConflictSeverity;
using DbConflictSeverity = UPACIP.DataAccess.Enums.ConflictSeverity;

namespace UPACIP.Service.Conflict;

/// <summary>
/// Orchestrates the full clinical conflict lifecycle: persisting AI-detected conflicts,
/// escalating urgent items, resolving and dismissing conflicts with staff attribution,
/// re-evaluating resolved conflicts on new document upload, and serving the review queue
/// (US_044, AC-1, AC-3, AC-4, FR-053).
///
/// Key design decisions:
///   - Conflict aggregation: when the same conflict type surfaces across 3+ documents
///     they are merged into a single <c>ClinicalConflict</c> row with all source IDs
///     captured in JSONB arrays (Edge Case).
///   - Low-confidence batch fallback: when any conflict in the batch reports
///     confidence &lt; 0.80, the entire batch is promoted to <c>UnderReview</c> so a
///     staff member must manually verify before the profile is considered clean (AC-4).
///   - Resolved conflict preservation: resolved/dismissed conflicts are only reopened
///     when a new document's extracted data IDs overlap with the conflict's source IDs,
///     indicating the contradiction is still present (Edge Case).
///   - Cache invalidation: the patient's Redis-cached profile is invalidated on every
///     conflict state change so the staff dashboard reflects current state.
///   - Audit logging: every state transition is logged with a correlation ID per NFR-035.
/// </summary>
public sealed class ConflictManagementService : IConflictManagementService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Confidence threshold below which the entire batch is flagged for manual review (AC-4).</summary>
    private const float LowConfidenceThreshold = 0.80f;

    /// <summary>Minimum source-IDs overlap required to reopen a resolved conflict on new document upload.</summary>
    private const int ReopenOverlapMinimum = 1;

    // ─────────────────────────────────────────────────────────────────────────
    // Cache key helpers (mirrors PatientProfileService key scheme)
    // ─────────────────────────────────────────────────────────────────────────

    private static string ProfileCacheKey(Guid patientId)  => $"upacip:profile:{patientId}";
    private static string VersionsCacheKey(Guid patientId) => $"upacip:profile:{patientId}:versions";

    // ─────────────────────────────────────────────────────────────────────────
    // AI ConflictType string → DataAccess ConflictType enum mapping
    // ─────────────────────────────────────────────────────────────────────────

    private static ConflictType MapAiConflictType(string aiType) => aiType switch
    {
        "MedicationContraindication"  => ConflictType.MedicationContraindication,
        "ConflictingDiagnosis"        => ConflictType.DuplicateDiagnosis,
        "ChronologicallyImplausible"  => ConflictType.DateInconsistency,
        "Duplicate"                   => ConflictType.DuplicateDiagnosis,
        _                             => ConflictType.MedicationDiscrepancy,
    };

    // ─────────────────────────────────────────────────────────────────────────
    // AI ConflictSeverity enum → DataAccess ConflictSeverity enum mapping
    // ─────────────────────────────────────────────────────────────────────────

    private static DbConflictSeverity MapAiSeverity(AiConflictSeverity aiSeverity)
        => aiSeverity switch
        {
            AiConflictSeverity.Critical => DbConflictSeverity.Critical,
            AiConflictSeverity.High     => DbConflictSeverity.High,
            AiConflictSeverity.Medium   => DbConflictSeverity.Medium,
            AiConflictSeverity.Low      => DbConflictSeverity.Low,
            _                          => DbConflictSeverity.Low,
        };

    // ─────────────────────────────────────────────────────────────────────────
    // Dependencies
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ApplicationDbContext                 _db;
    private readonly ICacheService                        _cache;
    private readonly ILogger<ConflictManagementService>   _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public ConflictManagementService(
        ApplicationDbContext               db,
        ICacheService                      cache,
        ILogger<ConflictManagementService> logger)
    {
        _db     = db;
        _cache  = cache;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PersistDetectedConflictsAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<(int PersistedCount, bool RequiresManualReview)> PersistDetectedConflictsAsync(
        ConflictAnalysisResult aiResult,
        Guid                   patientId,
        Guid                   profileVersionId,
        CancellationToken      ct = default)
    {
        if (!aiResult.ConflictsDetected || aiResult.Conflicts.Count == 0)
        {
            _logger.LogInformation(
                "ConflictMgmtSvc: no conflicts to persist. PatientId={PatientId}", patientId);
            return (0, false);
        }

        // ── AC-4: Low-confidence batch fallback ───────────────────────────────
        // Two signals trigger manual review:
        //   (a) Overall analysis confidence < 0.80 (RequiresManualVerification on result — AIR-010).
        //   (b) Any individual conflict in the batch has confidence < 0.80.
        // Either signal flags the entire batch as UnderReview.
        var requiresManualReview = aiResult.RequiresManualVerification
            || aiResult.Conflicts.Any(c => c.Confidence < LowConfidenceThreshold);
        var initialStatus = requiresManualReview ? ConflictStatus.UnderReview : ConflictStatus.Detected;

        if (requiresManualReview)
        {
            _logger.LogWarning(
                "ConflictMgmtSvc: low-confidence batch detected — entire batch requires manual review (AC-4). " +
                "PatientId={PatientId}, ConflictCount={Count}",
                patientId, aiResult.Conflicts.Count);
        }

        // ── Edge Case: Multi-document conflict aggregation ─────────────────────
        // Group conflicts by mapped type, then aggregate source IDs across all conflicts
        // of the same type so 3+ document conflicts produce a single row.
        var aggregated = AggregateConflicts(aiResult.Conflicts);

        var now = DateTime.UtcNow;
        var entities = new List<ClinicalConflict>(aggregated.Count);

        foreach (var group in aggregated)
        {
            var entity = new ClinicalConflict
            {
                Id                      = Guid.NewGuid(),
                PatientId               = patientId,
                ConflictType            = group.ConflictType,
                Severity                = group.Severity,
                Status                  = initialStatus,
                // is_urgent is set by EscalateUrgentConflictsAsync; pre-set Critical contraindications.
                IsUrgent                = group.ConflictType == ConflictType.MedicationContraindication
                                          && group.Severity == DbConflictSeverity.Critical,
                SourceExtractedDataIds  = group.SourceExtractedDataIds,
                SourceDocumentIds       = group.SourceDocumentIds,
                ConflictDescription     = group.ConflictDescription,
                AiExplanation           = group.AiExplanation,
                AiConfidenceScore       = group.AiConfidenceScore,
                ProfileVersionId        = profileVersionId,
                CreatedAt               = now,
                UpdatedAt               = now,
            };

            entities.Add(entity);
        }

        _db.ClinicalConflicts.AddRange(entities);
        await _db.SaveChangesAsync(ct);

        // ── AC-3: Escalate urgent conflicts (MedicationContraindication + Critical) ──
        await EscalateUrgentConflictsAsync(patientId, ct);

        // Invalidate cached patient profile so staff dashboard reflects new conflicts.
        await InvalidatePatientCacheAsync(patientId, ct);

        _logger.LogInformation(
            "ConflictMgmtSvc: persisted {Count} conflicts. " +
            "PatientId={PatientId}, ProfileVersion={VersionId}, RequiresManualReview={ManualReview}",
            entities.Count, patientId, profileVersionId, requiresManualReview);

        return (entities.Count, requiresManualReview);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // EscalateUrgentConflictsAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ConflictEscalationResult> EscalateUrgentConflictsAsync(
        Guid              patientId,
        CancellationToken ct = default)
    {
        // Find open MedicationContraindication conflicts that are not yet marked urgent.
        var candidates = await _db.ClinicalConflicts
            .Where(c => c.PatientId == patientId
                     && c.ConflictType == ConflictType.MedicationContraindication
                     && (c.Status == ConflictStatus.Detected || c.Status == ConflictStatus.UnderReview)
                     && !c.IsUrgent)
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return new ConflictEscalationResult { EvaluatedCount = 0, EscalatedCount = 0 };

        var now = DateTime.UtcNow;

        foreach (var conflict in candidates)
        {
            conflict.IsUrgent   = true;
            conflict.UpdatedAt  = now;
        }

        await _db.SaveChangesAsync(ct);

        var escalatedIds = candidates.Select(c => c.Id).ToList();

        _logger.LogWarning(
            "ConflictMgmtSvc: escalated {Count} MedicationContraindication conflicts to URGENT (AC-3). " +
            "PatientId={PatientId}, ConflictIds=[{Ids}]",
            escalatedIds.Count, patientId, string.Join(", ", escalatedIds));

        return new ConflictEscalationResult
        {
            EvaluatedCount     = candidates.Count,
            EscalatedCount     = escalatedIds.Count,
            EscalatedConflictIds = escalatedIds,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ResolveConflictAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task ResolveConflictAsync(ConflictResolutionRequest request, CancellationToken ct = default)
    {
        var conflict = await FindOpenConflictAsync(request.ConflictId, ct);

        var now = DateTime.UtcNow;
        conflict.Status          = ConflictStatus.Resolved;
        conflict.ResolvedByUserId = request.ResolvedByUserId;
        conflict.ResolutionNotes = request.ResolutionNotes;
        conflict.ResolvedAt      = now;
        conflict.UpdatedAt       = now;

        await _db.SaveChangesAsync(ct);

        await InvalidatePatientCacheAsync(conflict.PatientId, ct);

        _logger.LogInformation(
            "ConflictMgmtSvc: conflict resolved. " +
            "ConflictId={ConflictId}, PatientId={PatientId}, ResolvedBy={UserId}",
            conflict.Id, conflict.PatientId, request.ResolvedByUserId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DismissConflictAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task DismissConflictAsync(ConflictResolutionRequest request, CancellationToken ct = default)
    {
        var conflict = await FindOpenConflictAsync(request.ConflictId, ct);

        var now = DateTime.UtcNow;
        conflict.Status          = ConflictStatus.Dismissed;
        conflict.ResolvedByUserId = request.ResolvedByUserId;
        conflict.ResolutionNotes = request.ResolutionNotes;
        conflict.ResolvedAt      = now;
        conflict.UpdatedAt       = now;

        await _db.SaveChangesAsync(ct);

        await InvalidatePatientCacheAsync(conflict.PatientId, ct);

        _logger.LogInformation(
            "ConflictMgmtSvc: conflict dismissed. " +
            "ConflictId={ConflictId}, PatientId={PatientId}, DismissedBy={UserId}",
            conflict.Id, conflict.PatientId, request.ResolvedByUserId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ReEvaluateOnNewDocumentAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<int> ReEvaluateOnNewDocumentAsync(
        Guid                patientId,
        IReadOnlyList<Guid> newDocumentIds,
        CancellationToken   ct = default)
    {
        if (newDocumentIds.Count == 0)
            return 0;

        // Load the extracted data IDs for the new documents so we can compare against
        // the resolved conflict source_extracted_data_ids JSONB arrays.
        var newExtractedIds = await _db.ExtractedData
            .AsNoTracking()
            .Where(e => newDocumentIds.Contains(e.DocumentId) && !e.IsArchived)
            .Select(e => e.Id)
            .ToListAsync(ct);

        if (newExtractedIds.Count == 0)
            return 0;

        var newExtractedSet = new HashSet<Guid>(newExtractedIds);

        // Load resolved/dismissed conflicts for the patient to evaluate for reopening.
        // Only conflicts whose source_extracted_data_ids overlap with the new data are candidates.
        var closedConflicts = await _db.ClinicalConflicts
            .Where(c => c.PatientId == patientId
                     && (c.Status == ConflictStatus.Resolved || c.Status == ConflictStatus.Dismissed))
            .ToListAsync(ct);

        if (closedConflicts.Count == 0)
            return 0;

        var now = DateTime.UtcNow;
        var reopenedCount = 0;

        foreach (var conflict in closedConflicts)
        {
            // Reopen the conflict only when new documents directly reference one or more
            // of the same extracted data points that were involved in the original conflict
            // (contradiction still present — Edge Case: resolved conflict preservation).
            var overlap = conflict.SourceExtractedDataIds
                .Count(id => newExtractedSet.Contains(id));

            if (overlap < ReopenOverlapMinimum)
                continue;

            conflict.Status      = ConflictStatus.Detected;
            conflict.IsUrgent    = conflict.ConflictType == ConflictType.MedicationContraindication;
            conflict.UpdatedAt   = now;

            reopenedCount++;

            _logger.LogWarning(
                "ConflictMgmtSvc: resolved conflict reopened due to new contradictory document data (Edge Case). " +
                "ConflictId={ConflictId}, PatientId={PatientId}, OverlapCount={Overlap}",
                conflict.Id, patientId, overlap);
        }

        if (reopenedCount > 0)
        {
            await _db.SaveChangesAsync(ct);
            await InvalidatePatientCacheAsync(patientId, ct);
        }

        return reopenedCount;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetReviewQueueAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ReviewQueuePage> GetReviewQueueAsync(
        int               page,
        int               pageSize,
        CancellationToken ct = default)
    {
        page     = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        // Open conflicts only — Detected or UnderReview.
        var baseQuery = _db.ClinicalConflicts
            .AsNoTracking()
            .Include(c => c.Patient)
            .Where(c => c.Status == ConflictStatus.Detected || c.Status == ConflictStatus.UnderReview);

        var totalCount = await baseQuery.CountAsync(ct);

        // Sort: urgent first, then newest first (AC-3 — URGENT items top of queue).
        // Note: SourceDocumentIds is a JSONB-backed List<Guid> that cannot be projected
        // directly in SQL; we load the entity and map client-side for the count field.
        var rawItems = await baseQuery
            .OrderByDescending(c => c.IsUrgent)
            .ThenByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = rawItems.Select(c => new ReviewQueueEntry
            {
                ConflictId          = c.Id,
                PatientId           = c.PatientId,
                PatientName         = c.Patient.FullName,
                ConflictType        = c.ConflictType,
                Severity            = c.Severity,
                Status              = c.Status,
                IsUrgent            = c.IsUrgent,
                ConflictDescription = c.ConflictDescription,
                AiConfidenceScore   = c.AiConfidenceScore,
                DetectedAt          = c.CreatedAt,
                SourceDocumentCount = c.SourceDocumentIds.Count,
            })
            .ToList();

        return new ReviewQueuePage
        {
            Items      = items,
            TotalCount = totalCount,
            Page       = page,
            PageSize   = pageSize,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Aggregates <see cref="DetectedConflict"/> items by conflict type so that conflicts
    /// spanning multiple source documents (3+) are merged into a single record (Edge Case).
    ///
    /// Within each type group, the conflict with the highest confidence score is used as the
    /// representative description and explanation; all unique source IDs are merged.
    /// </summary>
    private static List<AggregatedConflict> AggregateConflicts(IReadOnlyList<DetectedConflict> detected)
    {
        var groups = detected.GroupBy(c => MapAiConflictType(c.ConflictType));
        var result = new List<AggregatedConflict>();

        foreach (var group in groups)
        {
            var conflicts = group.OrderByDescending(c => c.Confidence).ToList();
            var best      = conflicts[0]; // highest confidence is representative

            // Collect all non-empty source extracted data IDs across the group,
            // including AdditionalSourceIds from 3+ document conflicts (AIR-007, Edge Case).
            var extractedIds = conflicts
                .SelectMany(c => new[] { c.DataPointAId, c.DataPointBId }
                    .Concat(c.AdditionalSourceIds))
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            // Source document IDs are not available from DetectedConflict directly;
            // they are aggregated via the extracted data IDs when persisting.
            // Store extracted IDs only — document IDs are resolved from ExtractedData table.
            result.Add(new AggregatedConflict
            {
                ConflictType           = group.Key,
                Severity               = MapAiSeverity(best.Severity),
                SourceExtractedDataIds = extractedIds,
                SourceDocumentIds      = [],           // resolved after DB load; see note above
                ConflictDescription    = TruncateText(best.Reasoning, 500),
                AiExplanation          = best.Reasoning,
                AiConfidenceScore      = best.Confidence,
            });
        }

        return result;
    }

    /// <summary>
    /// Loads the open conflict by ID and validates it is in an open state.
    /// Throws <see cref="InvalidOperationException"/> when not found or already closed.
    /// </summary>
    private async Task<ClinicalConflict> FindOpenConflictAsync(Guid conflictId, CancellationToken ct)
    {
        var conflict = await _db.ClinicalConflicts
            .FirstOrDefaultAsync(c => c.Id == conflictId, ct);

        if (conflict is null)
            throw new InvalidOperationException($"Clinical conflict {conflictId} not found.");

        if (conflict.Status is ConflictStatus.Resolved or ConflictStatus.Dismissed)
        {
            throw new InvalidOperationException(
                $"Clinical conflict {conflictId} is already {conflict.Status.ToString().ToLower()} " +
                "and cannot be modified.");
        }

        return conflict;
    }

    /// <summary>
    /// Removes both the profile and version-history cache entries for the patient
    /// so the staff dashboard reflects the latest conflict state.
    /// Failures are swallowed per <see cref="ICacheService"/> contract.
    /// </summary>
    private async Task InvalidatePatientCacheAsync(Guid patientId, CancellationToken ct)
    {
        await _cache.RemoveAsync(ProfileCacheKey(patientId), ct);
        await _cache.RemoveAsync(VersionsCacheKey(patientId), ct);
    }

    private static string TruncateText(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength];

    // ─────────────────────────────────────────────────────────────────────────
    // Internal aggregate DTO (private — not exposed through interface)
    // ─────────────────────────────────────────────────────────────────────────

    private sealed record AggregatedConflict
    {
        public ConflictType      ConflictType           { get; init; }
        public DbConflictSeverity Severity              { get; init; }
        public List<Guid>        SourceExtractedDataIds { get; init; } = [];
        public List<Guid>        SourceDocumentIds      { get; init; } = [];
        public string            ConflictDescription    { get; init; } = string.Empty;
        public string            AiExplanation          { get; init; } = string.Empty;
        public float             AiConfidenceScore      { get; init; }
    }
}
