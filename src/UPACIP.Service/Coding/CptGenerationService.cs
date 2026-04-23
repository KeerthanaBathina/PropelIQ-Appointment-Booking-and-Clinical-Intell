using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.AI.Coding;
using UPACIP.Service.Caching;

namespace UPACIP.Service.Coding;

/// <summary>
/// Orchestrates the CPT procedure code generation workflow: reads extracted procedure data,
/// delegates to the AI gateway, validates results against the CPT code library, detects
/// bundled procedure opportunities, persists to <c>medical_codes</c>, and handles the
/// uncodable edge case (US_048, AC-1, AC-3, AIR-003).
///
/// Concurrency / idempotency (NFR-034):
///   When a <c>MedicalCode</c> row already exists for (patientId, Cpt, codeValue) the
///   existing row is updated rather than duplicated.
///
/// Bundle detection:
///   After AI suggestions are persisted, the service queries <c>cpt_bundle_rules</c> to
///   identify groups of active procedures that can be billed as a bundle.  All codes
///   belonging to the same bundle receive the same shared <c>BundleGroupId</c> (AC-3 edge case).
///
/// Confidence threshold (AIR-Q07/AIR-Q08):
///   Codes with confidence &lt; 0.80 are flagged with <c>RevalidationStatus.PendingReview</c>
///   so the staff review queue can route them for mandatory manual verification.
/// </summary>
public sealed class CptGenerationService : ICptGenerationService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    private const string UncodableValue       = "UNCODABLE";
    private const string UncodableDescription = "No matching CPT code found";
    private const float  UncodableConfidence  = 0.00f;

    /// <summary>Minimum confidence to accept as a valid suggestion (above background noise).</summary>
    private const float MinConfidenceThreshold = 0.01f;

    /// <summary>
    /// Confidence below which the code is flagged for mandatory manual review (AIR-Q07, AIR-Q08).
    /// </summary>
    private const float ManualReviewThreshold = 0.80f;

    private static readonly TimeSpan PendingCacheTtl = TimeSpan.FromMinutes(5); // NFR-030

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ApplicationDbContext          _db;
    private readonly IAiCodingGateway              _aiGateway;
    private readonly ICacheService                 _cache;
    private readonly ILogger<CptGenerationService> _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public CptGenerationService(
        ApplicationDbContext         db,
        IAiCodingGateway             aiGateway,
        ICacheService                cache,
        ILogger<CptGenerationService> logger)
    {
        _db        = db;
        _aiGateway = aiGateway;
        _cache     = cache;
        _logger    = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ICptGenerationService
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<CptCodingRunResult> GenerateCptCodesAsync(
        Guid                patientId,
        IReadOnlyList<Guid> procedureIds,
        string              correlationId,
        CancellationToken   ct = default)
    {
        _logger.LogInformation(
            "CptGenerationService: starting coding run. PatientId={PatientId} ProcedureCount={Count} CorrelationId={CorrelationId}",
            patientId, procedureIds.Count, correlationId);

        // ── 1. Load extracted procedure descriptions ─────────────────────────
        var extractedRows = await _db.Set<ExtractedData>()
            .Where(e => procedureIds.Contains(e.Id)
                     && e.DataType == DataType.Procedure)
            .AsNoTracking()
            .ToListAsync(ct);

        if (extractedRows.Count == 0)
        {
            _logger.LogWarning(
                "CptGenerationService: no Procedure-type ExtractedData found for supplied IDs. " +
                "PatientId={PatientId} CorrelationId={CorrelationId}",
                patientId, correlationId);
            return new CptCodingRunResult
            {
                CodesInserted        = 0,
                UnmappedProcedureIds = procedureIds.ToList(),
            };
        }

        // Build description map: ID → normalised text.
        var descriptions = extractedRows.ToDictionary(
            e => e.Id,
            e => e.DataContent?.NormalizedValue ?? e.DataContent?.RawText ?? string.Empty);

        // ── 2. Call AI gateway ───────────────────────────────────────────────
        IReadOnlyList<AiCptCodingResult> aiResults;
        try
        {
            aiResults = await _aiGateway.GenerateCptCodesAsync(descriptions, patientId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "CptGenerationService: AI gateway threw an unexpected exception. " +
                "Treating all procedures as uncodable. PatientId={PatientId} CorrelationId={CorrelationId}",
                patientId, correlationId);

            aiResults = procedureIds
                .Select(id => new AiCptCodingResult { ProcedureId = id, Suggestions = [] })
                .ToList();
        }

        var resultMap = aiResults.ToDictionary(r => r.ProcedureId);

        // ── 3. Validate each suggestion against the current CPT library ───────
        var activeCptCodes = await _db.Set<CptCodeLibrary>()
            .Where(l => l.IsActive)
            .AsNoTracking()
            .ToDictionaryAsync(l => l.CptCode, l => l, StringComparer.OrdinalIgnoreCase, ct);

        // ── 4. Persist to medical_codes (upsert pattern) ─────────────────────
        var codesInserted = 0;
        var unmappedIds   = new List<Guid>();

        foreach (var extractedRow in extractedRows)
        {
            if (!resultMap.TryGetValue(extractedRow.Id, out var aiResult)
                || aiResult.Suggestions.Count == 0)
            {
                await UpsertUncodableAsync(patientId, extractedRow.Id, correlationId, ct);
                unmappedIds.Add(extractedRow.Id);
                continue;
            }

            bool hasValidSuggestion = false;
            foreach (var suggestion in aiResult.Suggestions.OrderBy(s => s.RelevanceRank ?? 1))
            {
                if (suggestion.Confidence < MinConfidenceThreshold)
                    continue;

                // Validate against current CPT library (AIR-S02).
                var libraryVersion = activeCptCodes.TryGetValue(suggestion.CodeValue, out var libEntry)
                    ? libEntry.CptCode  // use CptCode field as library identifier
                    : null;

                // Flag low-confidence codes for mandatory manual review (AIR-Q07/AIR-Q08).
                var flagForReview = suggestion.Confidence < ManualReviewThreshold;

                await UpsertCptCodeAsync(
                    patientId,
                    extractedRow.Id,
                    suggestion,
                    libraryVersion,
                    flagForReview,
                    correlationId,
                    ct);

                codesInserted++;
                hasValidSuggestion = true;
            }

            if (!hasValidSuggestion)
            {
                await UpsertUncodableAsync(patientId, extractedRow.Id, correlationId, ct);
                unmappedIds.Add(extractedRow.Id);
            }
        }

        await _db.SaveChangesAsync(ct);

        // ── 5. Detect bundle groups (AC-3 edge case) ─────────────────────────
        await DetectBundlesAsync(patientId, ct);

        // ── 6. Invalidate pending-codes cache ────────────────────────────────
        await _cache.RemoveAsync(PendingCacheKey(patientId), ct);

        _logger.LogInformation(
            "CptGenerationService: coding run complete. " +
            "PatientId={PatientId} Inserted={Inserted} Unmapped={Unmapped} CorrelationId={CorrelationId}",
            patientId, codesInserted, unmappedIds.Count, correlationId);

        return new CptCodingRunResult
        {
            CodesInserted        = codesInserted,
            UnmappedProcedureIds = unmappedIds,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task UpsertCptCodeAsync(
        Guid                patientId,
        Guid                procedureId,
        AiCptCodeSuggestion suggestion,
        string?             libraryVersion,
        bool                flagForReview,
        string              correlationId,
        CancellationToken   ct)
    {
        // Idempotency: match on (patientId, Cpt, codeValue) — NFR-034.
        var existing = await _db.Set<MedicalCode>()
            .FirstOrDefaultAsync(
                m => m.PatientId == patientId
                  && m.CodeType  == CodeType.Cpt
                  && m.CodeValue == suggestion.CodeValue,
                ct);

        var revalidationStatus = flagForReview ? RevalidationStatus.PendingReview : (RevalidationStatus?)null;

        if (existing is not null)
        {
            existing.Description         = suggestion.Description;
            existing.Justification       = suggestion.Justification;
            existing.AiConfidenceScore   = suggestion.Confidence;
            existing.RelevanceRank       = suggestion.RelevanceRank;
            existing.LibraryVersion      = libraryVersion;
            existing.IsBundled           = suggestion.IsBundled;
            existing.RevalidationStatus  = revalidationStatus;
            existing.UpdatedAt           = DateTime.UtcNow;
        }
        else
        {
            await _db.Set<MedicalCode>().AddAsync(new MedicalCode
            {
                PatientId          = patientId,
                CodeType           = CodeType.Cpt,
                CodeValue          = suggestion.CodeValue,
                Description        = suggestion.Description,
                Justification      = suggestion.Justification,
                SuggestedByAi      = true,
                AiConfidenceScore  = suggestion.Confidence,
                RelevanceRank      = suggestion.RelevanceRank,
                LibraryVersion     = libraryVersion,
                IsBundled          = suggestion.IsBundled,
                RevalidationStatus = revalidationStatus,
            }, ct);
        }

        _logger.LogDebug(
            "CptGenerationService: upserted CPT code {CodeValue} for procedure {ProcedureId} " +
            "(confidence={Confidence:F2}, flagged={Flagged}). CorrelationId={CorrelationId}",
            suggestion.CodeValue, procedureId, suggestion.Confidence, flagForReview, correlationId);
    }

    private async Task UpsertUncodableAsync(
        Guid              patientId,
        Guid              procedureId,
        string            correlationId,
        CancellationToken ct)
    {
        var existing = await _db.Set<MedicalCode>()
            .FirstOrDefaultAsync(
                m => m.PatientId == patientId
                  && m.CodeType  == CodeType.Cpt
                  && m.CodeValue == UncodableValue,
                ct);

        if (existing is null)
        {
            await _db.Set<MedicalCode>().AddAsync(new MedicalCode
            {
                PatientId         = patientId,
                CodeType          = CodeType.Cpt,
                CodeValue         = UncodableValue,
                Description       = UncodableDescription,
                Justification     = UncodableDescription,
                SuggestedByAi     = true,
                AiConfidenceScore = UncodableConfidence,
            }, ct);
        }
        else
        {
            existing.UpdatedAt = DateTime.UtcNow;
        }

        _logger.LogDebug(
            "CptGenerationService: inserted UNCODABLE sentinel for procedure {ProcedureId}. " +
            "CorrelationId={CorrelationId}",
            procedureId, correlationId);
    }

    /// <summary>
    /// Cross-references the patient's pending CPT codes against <c>cpt_bundle_rules</c>
    /// and assigns a shared <c>BundleGroupId</c> to codes that belong to the same bundle (AC-3 edge case).
    /// </summary>
    private async Task DetectBundlesAsync(Guid patientId, CancellationToken ct)
    {
        try
        {
            // Load active bundle rules.
            var bundleRules = await _db.Set<CptBundleRule>()
                .Where(r => r.IsActive)
                .AsNoTracking()
                .ToListAsync(ct);

            if (bundleRules.Count == 0) return;

            // Load this patient's pending CPT codes (not yet approved).
            var pendingCodes = await _db.Set<MedicalCode>()
                .Where(m => m.PatientId        == patientId
                         && m.CodeType         == CodeType.Cpt
                         && m.ApprovedByUserId == null
                         && m.CodeValue        != UncodableValue)
                .ToListAsync(ct);

            if (pendingCodes.Count == 0) return;

            var pendingCodeValues = pendingCodes.Select(c => c.CodeValue).ToHashSet(StringComparer.OrdinalIgnoreCase);

            // For each bundle rule, check if both the bundle code and its component are present.
            // Group by BundleCptCode so all rules for the same bundle are processed together.
            var rulesByBundle = bundleRules
                .GroupBy(r => r.BundleCptCode, StringComparer.OrdinalIgnoreCase);

            foreach (var bundleGroup in rulesByBundle)
            {
                var bundleCode = bundleGroup.Key;
                var componentCodes = bundleGroup.Select(r => r.ComponentCptCode).ToList();

                // Check if the bundle code and at least one component are in pending codes.
                if (!pendingCodeValues.Contains(bundleCode)) continue;
                if (!componentCodes.Any(c => pendingCodeValues.Contains(c))) continue;

                // Assign a shared BundleGroupId to the bundle code and matching components.
                var bundleGroupId = Guid.NewGuid();

                foreach (var code in pendingCodes)
                {
                    if (code.CodeValue.Equals(bundleCode, StringComparison.OrdinalIgnoreCase))
                    {
                        code.IsBundled     = true;
                        code.BundleGroupId = bundleGroupId;
                    }
                    else if (componentCodes.Contains(code.CodeValue, StringComparer.OrdinalIgnoreCase))
                    {
                        code.IsBundled     = true;
                        code.BundleGroupId = bundleGroupId;
                    }
                }
            }

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Bundle detection failure must not block the main pipeline.
            _logger.LogWarning(ex,
                "CptGenerationService: bundle detection failed for PatientId={PatientId}. " +
                "Continuing without bundle assignment.", patientId);
        }
    }

    private static string PendingCacheKey(Guid patientId)
        => $"upacip:cpt:pending:{patientId}";
}
