using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Caching;

namespace UPACIP.Service.Coding;

/// <summary>
/// Orchestrates the ICD-10 code generation workflow: reads extracted diagnosis data,
/// delegates to the AI gateway, validates results against the code library, ranks
/// multiple codes per diagnosis, persists to <c>medical_codes</c>, and handles the
/// uncodable edge case (US_047, AC-1, AC-4).
///
/// Concurrency / idempotency:
///   When a <c>MedicalCode</c> row already exists for (patientId, Icd10, codeValue) the
///   existing row is updated rather than duplicated.  This ensures <c>POST generate</c>
///   is idempotent when the same idempotency key is reused (NFR-034).
///
/// Uncodable path (edge case):
///   When the AI returns no suggestions or confidence == 0, a sentinel row is inserted
///   with <c>code_value = "UNCODABLE"</c>, <c>ai_confidence_score = 0.00</c>, and
///   <c>justification = "No matching ICD-10 code found"</c> so staff can easily filter
///   for manual coding work.
///
/// Cache:
///   The pending-codes cache key for the patient is invalidated on every successful
///   coding run so the <c>GET /pending</c> endpoint reflects new results immediately.
/// </summary>
public sealed class Icd10CodingService : IIcd10CodingService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    private const string UncodableValue       = "UNCODABLE";
    private const string UncodableDescription = "No matching ICD-10 code found";
    private const float  UncodableConfidence  = 0.00f;

    /// <summary>
    /// AI confidence below which the suggestion is treated as uncodable and does NOT
    /// create an active code entry.  A separate "UNCODABLE" sentinel is inserted instead.
    /// </summary>
    private const float MinConfidenceThreshold = 0.01f;

    private static readonly TimeSpan PendingCacheTtl = TimeSpan.FromMinutes(5); // NFR-030

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ApplicationDbContext           _db;
    private readonly IAiCodingGateway               _aiGateway;
    private readonly ICacheService                  _cache;
    private readonly ILogger<Icd10CodingService>    _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public Icd10CodingService(
        ApplicationDbContext        db,
        IAiCodingGateway            aiGateway,
        ICacheService               cache,
        ILogger<Icd10CodingService> logger)
    {
        _db        = db;
        _aiGateway = aiGateway;
        _cache     = cache;
        _logger    = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IIcd10CodingService
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<Icd10CodingRunResult> GenerateIcd10CodesAsync(
        Guid                patientId,
        IReadOnlyList<Guid> diagnosisIds,
        string              correlationId,
        CancellationToken   ct = default)
    {
        _logger.LogInformation(
            "Icd10CodingService: starting coding run. PatientId={PatientId} DiagnosisCount={Count} CorrelationId={CorrelationId}",
            patientId, diagnosisIds.Count, correlationId);

        // ── 1. Load extracted diagnosis descriptions ─────────────────────────
        var extractedRows = await _db.Set<ExtractedData>()
            .Where(e => diagnosisIds.Contains(e.Id)
                     && e.DataType == DataType.Diagnosis)
            .AsNoTracking()
            .ToListAsync(ct);

        if (extractedRows.Count == 0)
        {
            _logger.LogWarning(
                "Icd10CodingService: no Diagnosis-type ExtractedData found for supplied IDs. " +
                "PatientId={PatientId} CorrelationId={CorrelationId}",
                patientId, correlationId);
            return new Icd10CodingRunResult
            {
                CodesInserted         = 0,
                UnmappedDiagnosisIds  = diagnosisIds.ToList(),
            };
        }

        // Build description map: ID → raw text (NormalizedValue preferred over RawText).
        var descriptions = extractedRows.ToDictionary(
            e => e.Id,
            e => e.DataContent?.NormalizedValue ?? e.DataContent?.RawText ?? string.Empty);

        // ── 2. Call AI gateway ───────────────────────────────────────────────
        IReadOnlyList<AiCodingResult> aiResults;
        try
        {
            aiResults = await _aiGateway.GenerateCodesAsync(descriptions, patientId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Icd10CodingService: AI gateway threw an unexpected exception. " +
                "Treating all diagnoses as uncodable. PatientId={PatientId} CorrelationId={CorrelationId}",
                patientId, correlationId);

            // Safe fallback: treat all diagnoses as uncodable so the pipeline never blocks.
            aiResults = diagnosisIds
                .Select(id => new AiCodingResult { DiagnosisId = id, Suggestions = [] })
                .ToList();
        }

        // ── 3. Build a set of IDs that have real suggestions ─────────────────
        var resultMap = aiResults.ToDictionary(r => r.DiagnosisId);

        // ── 4. Validate each suggestion against the current ICD-10 library ───
        var currentCodes = await _db.Set<Icd10CodeLibrary>()
            .Where(l => l.IsCurrent)
            .AsNoTracking()
            .ToDictionaryAsync(l => l.CodeValue, l => l, StringComparer.OrdinalIgnoreCase, ct);

        // ── 5. Persist to medical_codes (upsert pattern) ─────────────────────
        var codesInserted = 0;
        var unmappedIds   = new List<Guid>();

        foreach (var extractedRow in extractedRows)
        {
            if (!resultMap.TryGetValue(extractedRow.Id, out var aiResult)
                || aiResult.Suggestions.Count == 0)
            {
                // No AI suggestion → insert uncodable sentinel.
                await UpsertUncodableAsync(patientId, extractedRow.Id, correlationId, ct);
                unmappedIds.Add(extractedRow.Id);
                continue;
            }

            // Validate and persist each ranked suggestion.
            bool hasValidSuggestion = false;
            foreach (var suggestion in aiResult.Suggestions.OrderBy(s => s.RelevanceRank ?? 1))
            {
                if (suggestion.Confidence < MinConfidenceThreshold)
                    continue;

                // Validate against current library (DR-015, AIR-S02).
                var libraryVersion = currentCodes.TryGetValue(suggestion.CodeValue, out var libEntry)
                    ? libEntry.LibraryVersion
                    : null;

                await UpsertMedicalCodeAsync(
                    patientId,
                    extractedRow.Id,
                    suggestion,
                    libraryVersion,
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

        // ── 6. Invalidate pending-codes cache ────────────────────────────────
        await _cache.RemoveAsync(PendingCacheKey(patientId), ct);

        _logger.LogInformation(
            "Icd10CodingService: coding run complete. " +
            "PatientId={PatientId} Inserted={Inserted} Unmapped={Unmapped} CorrelationId={CorrelationId}",
            patientId, codesInserted, unmappedIds.Count, correlationId);

        return new Icd10CodingRunResult
        {
            CodesInserted         = codesInserted,
            UnmappedDiagnosisIds  = unmappedIds,
        };
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<MedicalCode>> GetPendingCodesAsync(
        Guid              patientId,
        CancellationToken ct = default)
    {
        return _cache.GetOrSetAsync<IReadOnlyList<MedicalCode>>(
            PendingCacheKey(patientId),
            factory: async () =>
            {
                var rows = await _db.Set<MedicalCode>()
                    .Where(m => m.PatientId == patientId
                             && m.CodeType  == CodeType.Icd10
                             && m.ApprovedByUserId == null)
                    .OrderBy(m => m.RelevanceRank ?? int.MaxValue)
                    .ThenByDescending(m => m.CreatedAt)
                    .AsNoTracking()
                    .ToListAsync(ct);

                return (IReadOnlyList<MedicalCode>)rows;
            },
            expiration: PendingCacheTtl,
            cancellationToken: ct)!;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task UpsertMedicalCodeAsync(
        Guid              patientId,
        Guid              diagnosisId,
        AiCodeSuggestion  suggestion,
        string?           libraryVersion,
        string            correlationId,
        CancellationToken ct)
    {
        // Idempotency: match on (patientId, Icd10, codeValue) — NFR-034.
        var existing = await _db.Set<MedicalCode>()
            .FirstOrDefaultAsync(
                m => m.PatientId == patientId
                  && m.CodeType  == CodeType.Icd10
                  && m.CodeValue == suggestion.CodeValue,
                ct);

        if (existing is not null)
        {
            existing.Description      = suggestion.Description;
            existing.Justification    = suggestion.Justification;
            existing.AiConfidenceScore = suggestion.Confidence;
            existing.RelevanceRank    = suggestion.RelevanceRank;
            existing.LibraryVersion   = libraryVersion;
            existing.UpdatedAt        = DateTime.UtcNow;
        }
        else
        {
            await _db.Set<MedicalCode>().AddAsync(new MedicalCode
            {
                PatientId         = patientId,
                CodeType          = CodeType.Icd10,
                CodeValue         = suggestion.CodeValue,
                Description       = suggestion.Description,
                Justification     = suggestion.Justification,
                SuggestedByAi     = true,
                AiConfidenceScore = suggestion.Confidence,
                RelevanceRank     = suggestion.RelevanceRank,
                LibraryVersion    = libraryVersion,
            }, ct);
        }

        _logger.LogDebug(
            "Icd10CodingService: upserted code {CodeValue} for diagnosis {DiagnosisId}. " +
            "CorrelationId={CorrelationId}",
            suggestion.CodeValue, diagnosisId, correlationId);
    }

    private async Task UpsertUncodableAsync(
        Guid              patientId,
        Guid              diagnosisId,
        string            correlationId,
        CancellationToken ct)
    {
        var existing = await _db.Set<MedicalCode>()
            .FirstOrDefaultAsync(
                m => m.PatientId == patientId
                  && m.CodeType  == CodeType.Icd10
                  && m.CodeValue == UncodableValue,
                ct);

        if (existing is null)
        {
            await _db.Set<MedicalCode>().AddAsync(new MedicalCode
            {
                PatientId         = patientId,
                CodeType          = CodeType.Icd10,
                CodeValue         = UncodableValue,
                Description       = UncodableDescription,
                Justification     = UncodableDescription,
                SuggestedByAi     = true,
                AiConfidenceScore = UncodableConfidence,
            }, ct);
        }
        else
        {
            // Re-stamp UpdatedAt so the staff review queue shows fresh activity.
            existing.UpdatedAt = DateTime.UtcNow;
        }

        _logger.LogDebug(
            "Icd10CodingService: inserted UNCODABLE sentinel for diagnosis {DiagnosisId}. " +
            "CorrelationId={CorrelationId}",
            diagnosisId, correlationId);
    }

    private static string PendingCacheKey(Guid patientId)
        => $"upacip:icd10:pending:{patientId}";
}
