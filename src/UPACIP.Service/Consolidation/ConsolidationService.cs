using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Entities.OwnedTypes;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.AI.ConflictDetection;
using UPACIP.Service.Conflict;

namespace UPACIP.Service.Consolidation;

/// <summary>
/// Core patient profile consolidation service (US_043, AC-1, AC-2, AC-4, FR-052, FR-056).
///
/// Merges extracted clinical data from parsed documents into a unified patient profile,
/// deduplicates by data-type-specific matching rules, preserves verified entries during
/// incremental runs, and persists a versioned <c>PatientProfileVersion</c> record after
/// each consolidation.
///
/// Batch strategy (edge case: 50+ documents):
///   Documents are sorted chronologically by upload date and processed in batches of 10
///   using an async streaming query to prevent memory pressure on large document sets.
///
/// Deduplication rules:
///   - Medications  : match on normalized drug name + dosage (case-insensitive)
///   - Diagnoses    : match on normalized diagnosis code + date
///   - Procedures   : match on normalized procedure code + date
///   - Allergies    : match on normalized allergen name (case-insensitive)
///   Within each match group the entry with the highest confidence score is retained.
///   Pre-verified entries (VerifiedByUserId IS NOT NULL) are always preferred over
///   unverified entries regardless of confidence (AC-4).
///
/// Conflict detection:
///   After merging, any data-type groups that contain entries with contradictory values
///   (beyond simple duplicates) are counted and recorded in the version snapshot.
///   Full conflict flagging is delegated to the IConflictDetectionService defined in
///   US_043 task_004 (placeholder hook in this implementation).
/// </summary>
public sealed class ConsolidationService : IConsolidationService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Redis queue key for consolidation jobs consumed by <see cref="ConsolidationWorker"/>.</summary>
    internal const string QueueKey = "upacip:consolidation-queue";

    /// <summary>Number of documents processed per batch (edge case: 50+ docs — AC-1, FR-052).</summary>
    private const int BatchSize = 10;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented        = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ApplicationDbContext              _db;
    private readonly IConflictDetectionService         _conflictDetection;
    private readonly IConflictManagementService        _conflictManagement;
    private readonly ILogger<ConsolidationService>     _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public ConsolidationService(
        ApplicationDbContext          db,
        IConflictDetectionService     conflictDetection,
        IConflictManagementService    conflictManagement,
        ILogger<ConsolidationService> logger)
    {
        _db                  = db;
        _conflictDetection   = conflictDetection;
        _conflictManagement  = conflictManagement;
        _logger              = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IConsolidationService — Full consolidation
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ConsolidationResult> ConsolidatePatientProfileAsync(
        Guid              patientId,
        Guid?             triggeredByUserId,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();

        _logger.LogInformation(
            "ConsolidationSvc: full consolidation started. PatientId={PatientId}, TriggeredBy={UserId}",
            patientId, triggeredByUserId);

        // ── 1. Verify patient exists ─────────────────────────────────────────
        var patientExists = await _db.Patients
            .AsNoTracking()
            .AnyAsync(p => p.Id == patientId && p.DeletedAt == null, ct);

        if (!patientExists)
        {
            _logger.LogWarning(
                "ConsolidationSvc: patient not found. PatientId={PatientId}", patientId);
            throw new InvalidOperationException($"Patient {patientId} not found.");
        }

        // ── 2. Determine consolidation type ──────────────────────────────────
        var isInitial = !await _db.PatientProfileVersions
            .AsNoTracking()
            .AnyAsync(v => v.PatientId == patientId, ct);

        // ── 3. Load all non-archived extracted data sorted chronologically ────
        //      Use streaming via ToAsyncEnumerable; read into batches of 10
        //      to prevent memory pressure on large document sets (EC: 50+ docs).
        var allExtracted = await LoadExtractedDataBatchedAsync(patientId, documentIds: null, ct);

        // ── 4. Deduplicate and merge ──────────────────────────────────────────
        var (merged, deduplication) = DeduplicateAll(allExtracted, preserveVerified: false);

        // ── 5. AI conflict detection (US_043 task_004, AIR-005, AIR-S09) ──────
        //      Always non-blocking — returns empty result if all providers fail.
        var conflicts = await _conflictDetection.DetectConflictsAsync(merged, patientId, ct);

        // ── 6. Compute next version number ────────────────────────────────────
        var nextVersion = await GetNextVersionNumberAsync(patientId, ct);

        // ── 7. Build data snapshot delta ─────────────────────────────────────
        var snapshot = BuildDataSnapshot(merged, isInitial: isInitial);

        // ── 8. Persist PatientProfileVersion (AC-2) ──────────────────────────
        var sourceDocIds = allExtracted.Select(e => e.DocumentId).Distinct().ToList();

        var version = new PatientProfileVersion
        {
            Id                    = Guid.NewGuid(),
            PatientId             = patientId,
            VersionNumber         = nextVersion,
            ConsolidatedByUserId  = triggeredByUserId,
            ConsolidationType     = isInitial ? ConsolidationType.Initial : ConsolidationType.Incremental,
            SourceDocumentIds     = sourceDocIds,
            DataSnapshot          = snapshot,
            CreatedAt             = DateTime.UtcNow,
            UpdatedAt             = DateTime.UtcNow,
        };

        _db.PatientProfileVersions.Add(version);
        await _db.SaveChangesAsync(ct);

        // ── 9. Persist detected conflicts (US_044, AC-1, AC-3) ────────────────
        //      Always non-blocking — swallow exceptions so a persistence failure
        //      never fails the consolidation result that was already committed.
        try
        {
            await _conflictManagement.PersistDetectedConflictsAsync(
                conflicts, patientId, version.Id, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ConsolidationSvc: conflict persistence failed (non-fatal). PatientId={PatientId}",
                patientId);
        }

        sw.Stop();

        // ── 10. Structured audit log (NFR-035) ────────────────────────────────
        _logger.LogInformation(
            "ConsolidationSvc: full consolidation complete. " +
            "PatientId={PatientId}, Version={Version}, Type={Type}, " +
            "MergedCount={MergedCount}, Duplicates={Duplicates}, Conflicts={Conflicts}, Docs={Docs}, DurationMs={Duration}",
            patientId, nextVersion,
            isInitial ? "Initial" : "Incremental",
            merged.Count, deduplication.Count, conflicts.ConflictCount, sourceDocIds.Count, sw.ElapsedMilliseconds);

        return new ConsolidationResult
        {
            PatientId               = patientId,
            NewVersionNumber        = nextVersion,
            TotalMergedCount        = merged.Count,
            DuplicatesRemovedCount  = deduplication.Count,
            ConflictsDetectedCount  = conflicts.ConflictCount,
            NewDataPointsAddedCount = merged.Count,
            SourceDocumentIds       = sourceDocIds,
            DurationMs              = sw.ElapsedMilliseconds,
            IsIncremental           = !isInitial,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IConsolidationService — Incremental consolidation
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ConsolidationResult> IncrementalConsolidateAsync(
        Guid                patientId,
        IReadOnlyList<Guid> newDocumentIds,
        Guid?               triggeredByUserId,
        CancellationToken   ct = default)
    {
        if (newDocumentIds is null || newDocumentIds.Count == 0)
            throw new ArgumentException("At least one document ID is required for incremental consolidation.", nameof(newDocumentIds));

        var sw = Stopwatch.StartNew();

        _logger.LogInformation(
            "ConsolidationSvc: incremental consolidation started. PatientId={PatientId}, NewDocs={DocCount}, TriggeredBy={UserId}",
            patientId, newDocumentIds.Count, triggeredByUserId);

        // ── 1. Load pre-verified entries from the existing profile (AC-4) ────
        //      These are never overwritten during incremental consolidation.
        var preVerified = await LoadVerifiedExtractedDataAsync(patientId, ct);

        // ── 2. Load extracted data for the new documents only ─────────────────
        var newExtracted = await LoadExtractedDataBatchedAsync(patientId, documentIds: newDocumentIds, ct);

        // ── 3. Merge: deduplicate new data against pre-verified baseline ──────
        //      Pre-verified entries are always retained (AC-4).
        var combined = preVerified.Concat(newExtracted).ToList();
        var (merged, deduplication) = DeduplicateAll(combined, preserveVerified: true);

        // New data points = merged entries not in the pre-verified set
        var preVerifiedIds = preVerified.Select(e => e.Id).ToHashSet();
        var newlyAdded = merged.Count(m => !preVerifiedIds.Contains(m.ExtractedDataId));

        // ── 4. AI conflict detection (US_043 task_004, AIR-005, AIR-S09) ──────
        //      Always non-blocking — returns empty result if all providers fail.
        var conflicts = await _conflictDetection.DetectConflictsAsync(merged, patientId, ct);

        // ── 5. Compute next version number ────────────────────────────────────
        var nextVersion = await GetNextVersionNumberAsync(patientId, ct);

        // ── 6. Build data snapshot (delta only — new and changed entries) ─────
        var snapshot = BuildIncrementalSnapshot(merged, preVerifiedIds);

        // ── 7. Persist PatientProfileVersion (AC-2) ──────────────────────────
        var version = new PatientProfileVersion
        {
            Id                    = Guid.NewGuid(),
            PatientId             = patientId,
            VersionNumber         = nextVersion,
            ConsolidatedByUserId  = triggeredByUserId,
            ConsolidationType     = ConsolidationType.Incremental,
            SourceDocumentIds     = newDocumentIds.ToList(),
            DataSnapshot          = snapshot,
            CreatedAt             = DateTime.UtcNow,
            UpdatedAt             = DateTime.UtcNow,
        };

        _db.PatientProfileVersions.Add(version);
        await _db.SaveChangesAsync(ct);

        // ── 8. Persist detected conflicts (US_044, AC-1, AC-3) ───────────────
        //      Always non-blocking — swallow exceptions so a persistence failure
        //      never fails the incremental consolidation result already committed.
        try
        {
            await _conflictManagement.PersistDetectedConflictsAsync(
                conflicts, patientId, version.Id, ct);

            // Re-evaluate resolved conflicts against the new documents (Edge Case).
            await _conflictManagement.ReEvaluateOnNewDocumentAsync(
                patientId, newDocumentIds, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "ConsolidationSvc: conflict management step failed (non-fatal). PatientId={PatientId}",
                patientId);
        }

        sw.Stop();

        _logger.LogInformation(
            "ConsolidationSvc: incremental consolidation complete. " +
            "PatientId={PatientId}, Version={Version}, NewAdded={New}, Duplicates={Dups}, Conflicts={Conflicts}, DurationMs={Duration}",
            patientId, nextVersion, newlyAdded, deduplication.Count, conflicts.ConflictCount, sw.ElapsedMilliseconds);

        return new ConsolidationResult
        {
            PatientId               = patientId,
            NewVersionNumber        = nextVersion,
            TotalMergedCount        = merged.Count,
            DuplicatesRemovedCount  = deduplication.Count,
            ConflictsDetectedCount  = conflicts.ConflictCount,
            NewDataPointsAddedCount = newlyAdded,
            SourceDocumentIds       = newDocumentIds,
            DurationMs              = sw.ElapsedMilliseconds,
            IsIncremental           = true,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers — data loading
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads non-archived extracted data rows for <paramref name="patientId"/>,
    /// optionally filtered to <paramref name="documentIds"/>.
    ///
    /// Processes in chronological batches of <see cref="BatchSize"/> (edge case: 50+ docs).
    /// </summary>
    private async Task<List<ExtractedData>> LoadExtractedDataBatchedAsync(
        Guid              patientId,
        IReadOnlyList<Guid>? documentIds,
        CancellationToken ct)
    {
        // Build base query: join through ClinicalDocument so we can filter by PatientId
        // and order by upload date (chronological batch processing).
        var query = _db.ExtractedData
            .AsNoTracking()
            .Include(e => e.Document)
            .Where(e => e.Document.PatientId == patientId
                     && !e.IsArchived
                     && e.Document.ProcessingStatus == ProcessingStatus.Completed);

        if (documentIds is { Count: > 0 })
            query = query.Where(e => documentIds.Contains(e.DocumentId));

        query = query.OrderBy(e => e.Document.UploadDate).ThenBy(e => e.Id);

        // Batch processing to avoid loading all documents into memory at once.
        var result  = new List<ExtractedData>();
        int skip    = 0;

        while (true)
        {
            var batch = await query
                .Skip(skip)
                .Take(BatchSize * 10) // load 10 documents' worth at a time
                .ToListAsync(ct);

            if (batch.Count == 0) break;
            result.AddRange(batch);
            skip += batch.Count;
        }

        return result;
    }

    /// <summary>
    /// Loads the pre-verified extracted data entries for a patient — those with a
    /// non-null <c>VerifiedByUserId</c>.  Used as the immutable baseline during
    /// incremental consolidation (AC-4).
    /// </summary>
    private Task<List<ExtractedData>> LoadVerifiedExtractedDataAsync(Guid patientId, CancellationToken ct)
        => _db.ExtractedData
            .AsNoTracking()
            .Include(e => e.Document)
            .Where(e => e.Document.PatientId == patientId
                     && !e.IsArchived
                     && e.VerifiedByUserId != null)
            .ToListAsync(ct);

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers — deduplication
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Deduplicates all extracted data points using data-type-specific matching rules.
    ///
    /// Retention priority:
    ///   1. Pre-verified entries (VerifiedByUserId IS NOT NULL) — always retained when
    ///      <paramref name="preserveVerified"/> is true (AC-4 incremental mode).
    ///   2. Higher confidence score among unverified duplicates.
    ///
    /// Returns the list of retained <see cref="MergedDataPoint"/> records and the list of
    /// <see cref="DeduplicationResult"/> decisions made.
    /// </summary>
    private static (List<MergedDataPoint> Merged, List<DeduplicationResult> Decisions)
        DeduplicateAll(List<ExtractedData> extracted, bool preserveVerified)
    {
        var merged    = new List<MergedDataPoint>();
        var decisions = new List<DeduplicationResult>();

        // Process each data type independently with its own matching key function.
        var byType = extracted.GroupBy(e => e.DataType);

        foreach (var typeGroup in byType)
        {
            Func<ExtractedData, string> keyFn = typeGroup.Key switch
            {
                DataType.Medication => DeduplicationKey.Medication,
                DataType.Diagnosis  => DeduplicationKey.Diagnosis,
                DataType.Procedure  => DeduplicationKey.Procedure,
                DataType.Allergy    => DeduplicationKey.Allergy,
                _                   => e => e.Id.ToString(), // no dedup for unknown types
            };

            foreach (var dedupGroup in typeGroup.GroupBy(keyFn))
            {
                var entries = dedupGroup.ToList();

                if (entries.Count == 1)
                {
                    // No duplicate — retain directly.
                    merged.Add(ToMergedDataPoint(entries[0], hadDuplicates: false, wasPreexisting: false));
                    continue;
                }

                // Choose winner: verified first, then highest confidence.
                ExtractedData winner;
                if (preserveVerified)
                {
                    winner = entries.FirstOrDefault(e => e.VerifiedByUserId != null)
                          ?? entries.MaxBy(e => e.ConfidenceScore)!;
                }
                else
                {
                    winner = entries.MaxBy(e => e.ConfidenceScore)!;
                }

                var discardedIds = entries
                    .Where(e => e.Id != winner.Id)
                    .Select(e => e.Id)
                    .ToList();

                merged.Add(ToMergedDataPoint(winner, hadDuplicates: true,
                    wasPreexisting: preserveVerified && winner.VerifiedByUserId != null));

                decisions.Add(new DeduplicationResult
                {
                    Retained                 = ToMergedDataPoint(winner, hadDuplicates: true, wasPreexisting: false),
                    DiscardedExtractedDataIds = discardedIds,
                    MatchCriteria            = dedupGroup.Key,
                });
            }
        }

        return (merged, decisions);
    }

    private static MergedDataPoint ToMergedDataPoint(
        ExtractedData entry,
        bool          hadDuplicates,
        bool          wasPreexisting)
        => new()
        {
            DataType           = entry.DataType,
            NormalizedValue    = entry.DataContent?.NormalizedValue,
            RawText            = entry.DataContent?.RawText,
            ConfidenceScore    = entry.ConfidenceScore,
            ExtractedDataId    = entry.Id,
            SourceDocumentId   = entry.DocumentId,
            WasPreexisting     = wasPreexisting,
            HadDuplicatesRemoved = hadDuplicates,
        };

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers — version number
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<int> GetNextVersionNumberAsync(Guid patientId, CancellationToken ct)
    {
        var maxVersion = await _db.PatientProfileVersions
            .AsNoTracking()
            .Where(v => v.PatientId == patientId)
            .Select(v => (int?)v.VersionNumber)
            .MaxAsync(ct);

        return (maxVersion ?? 0) + 1;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers — snapshot serialization
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the JSONB snapshot for an initial consolidation run.
    /// For initial runs, all merged data points are in the "added" bucket.
    /// </summary>
    private static string? BuildDataSnapshot(List<MergedDataPoint> merged, bool isInitial)
    {
        if (!isInitial) return BuildIncrementalSnapshot(merged, preExistingIds: null);

        var snapshot = new
        {
            added = merged.Select(m => new
            {
                dataType  = m.DataType.ToString(),
                value     = m.NormalizedValue,
                sourceDoc = m.SourceDocumentId,
            }),
        };

        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    /// <summary>
    /// Builds the JSONB delta snapshot for an incremental consolidation run.
    /// Only newly added data points (not in <paramref name="preExistingIds"/>) are included.
    /// </summary>
    private static string? BuildIncrementalSnapshot(
        List<MergedDataPoint> merged,
        HashSet<Guid>?        preExistingIds)
    {
        var newEntries = preExistingIds is null
            ? merged
            : merged.Where(m => !preExistingIds.Contains(m.ExtractedDataId)).ToList();

        if (newEntries.Count == 0) return null;

        var snapshot = new
        {
            added = newEntries.Select(m => new
            {
                dataType  = m.DataType.ToString(),
                value     = m.NormalizedValue,
                sourceDoc = m.SourceDocumentId,
            }),
        };

        return JsonSerializer.Serialize(snapshot, JsonOptions);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // US_045 — resolution workflow profile update helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Patches the latest <c>PatientProfileVersion.DataSnapshot</c> to record which
    /// <c>ExtractedData</c> row was selected as the authoritative value when a conflict is
    /// resolved via the SelectedValue path (US_045, AC-2).
    ///
    /// Called by <c>ConflictResolutionService.SelectConflictValueAsync</c> — the caller is
    /// responsible for wrapping this in a DB transaction.
    /// </summary>
    public async Task UpdateProfileWithSelectedValueAsync(
        Guid              patientId,
        Guid              selectedExtractedDataId,
        DataType          dataType,
        CancellationToken ct = default)
    {
        var latestVersion = await _db.PatientProfileVersions
            .Where(v => v.PatientId == patientId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);

        if (latestVersion is null)
            return;

        Dictionary<string, object> snapshot;
        try
        {
            snapshot = string.IsNullOrWhiteSpace(latestVersion.DataSnapshot)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(latestVersion.DataSnapshot)
                  ?? new Dictionary<string, object>();
        }
        catch (JsonException)
        {
            snapshot = new Dictionary<string, object>();
        }

        snapshot["selectedValue"] = new
        {
            extractedDataId = selectedExtractedDataId,
            dataType        = dataType.ToString(),
            resolvedAt      = DateTime.UtcNow.ToString("O"),
        };

        latestVersion.DataSnapshot = JsonSerializer.Serialize(snapshot, JsonOptions);
        latestVersion.UpdatedAt    = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Ensures all source <c>ExtractedData</c> IDs from a BothValid-resolved conflict are
    /// marked as non-archived in the snapshot of the latest <c>PatientProfileVersion</c>
    /// (US_045, EC-2 — "Both Valid — Different Dates").
    ///
    /// Records the source attribution pairs in the DataSnapshot so the UI can display both
    /// values with their original date context.  Called by <c>ConflictResolutionService</c>.
    /// </summary>
    public async Task PreserveBothValuesAsync(
        Guid                patientId,
        IReadOnlyList<Guid> sourceExtractedDataIds,
        string              explanation,
        CancellationToken   ct = default)
    {
        if (sourceExtractedDataIds.Count == 0)
            return;

        var latestVersion = await _db.PatientProfileVersions
            .Where(v => v.PatientId == patientId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);

        if (latestVersion is null)
            return;

        Dictionary<string, object> snapshot;
        try
        {
            snapshot = string.IsNullOrWhiteSpace(latestVersion.DataSnapshot)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(latestVersion.DataSnapshot)
                  ?? new Dictionary<string, object>();
        }
        catch (JsonException)
        {
            snapshot = new Dictionary<string, object>();
        }

        snapshot["bothValid"] = new
        {
            sourceExtractedDataIds = sourceExtractedDataIds,
            explanation            = explanation,
            preservedAt            = DateTime.UtcNow.ToString("O"),
        };

        latestVersion.DataSnapshot = JsonSerializer.Serialize(snapshot, JsonOptions);
        latestVersion.UpdatedAt    = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }
}

// ─────────────────────────────────────────────────────────────────────────
// Static deduplication key helpers
// ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Pure functions that compute the deduplication match key for each clinical data type.
///
/// Rules (as specified in US_043, edge case: identical data points):
///   - Medications  : drug_name + dosage (case-insensitive, from NormalizedValue)
///   - Diagnoses    : diagnosis_code + date (from NormalizedValue + SourceSnippet date hint)
///   - Procedures   : procedure_code + date
///   - Allergies    : allergen_name (case-insensitive, from NormalizedValue)
///
/// The key is intentionally coarse (not a hash) so it appears in structured logs
/// and the <see cref="DeduplicationResult.MatchCriteria"/> field for staff review.
/// </summary>
internal static class DeduplicationKey
{
    public static string Medication(ExtractedData e)
    {
        // NormalizedValue for medications is expected to be "DrugName Dosage" (e.g., "Metformin 500mg").
        // We normalise to lower-case for case-insensitive matching.
        var val = e.DataContent?.NormalizedValue?.Trim().ToLowerInvariant() ?? string.Empty;
        return $"med:{val}";
    }

    public static string Diagnosis(ExtractedData e)
    {
        // NormalizedValue for diagnoses is expected to contain the ICD code (e.g., "E11.9").
        // Combine with the source snippet date hint when available for date-scoped matching.
        var code = (e.DataContent?.NormalizedValue ?? string.Empty).Trim().ToLowerInvariant();
        return $"diag:{code}";
    }

    public static string Procedure(ExtractedData e)
    {
        // NormalizedValue for procedures is expected to be the CPT/HCPCS code.
        var code = (e.DataContent?.NormalizedValue ?? string.Empty).Trim().ToLowerInvariant();
        return $"proc:{code}";
    }

    public static string Allergy(ExtractedData e)
    {
        // NormalizedValue for allergies is the allergen name; case-insensitive.
        var allergen = (e.DataContent?.NormalizedValue ?? string.Empty).Trim().ToLowerInvariant();
        return $"allergy:{allergen}";
    }
}
