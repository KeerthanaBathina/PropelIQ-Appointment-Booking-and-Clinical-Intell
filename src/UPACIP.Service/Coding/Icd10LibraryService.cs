using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Coding;

/// <summary>
/// Manages the ICD-10 code reference library lifecycle:
/// quarterly refresh (AC-3) and revalidation of pending <c>MedicalCode</c> records
/// against the updated library (edge case: deprecated-code handling).
///
/// Refresh strategy:
///   1. Open a serialisable transaction (DR-029 — all-or-nothing).
///   2. Mark existing <c>is_current = true</c> entries that are absent from the incoming
///      dataset as deprecated (<c>is_current = false</c>, <c>deprecated_date = today</c>).
///   3. Upsert all incoming codes for the new library version.
///   4. Call <see cref="RevalidatePendingCodesAsync"/> within the same transaction.
///   5. Commit or roll back on any failure.
///
/// Revalidation outcome per <c>MedicalCode</c> row:
///   - <c>Valid</c>             — code is present and <c>is_current = true</c>.
///   - <c>DeprecatedReplaced</c> — code is present but <c>is_current = false</c>;
///                                <c>library_version</c> is updated so staff see which
///                                version introduced the deprecation.
///   - <c>PendingReview</c>    — code not found in library at all; staff must verify.
/// </summary>
public sealed class Icd10LibraryService : IIcd10LibraryService
{
    private readonly ApplicationDbContext           _db;
    private readonly ILogger<Icd10LibraryService>   _logger;

    public Icd10LibraryService(
        ApplicationDbContext        db,
        ILogger<Icd10LibraryService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IIcd10LibraryService — RefreshLibraryAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<LibraryRefreshResult> RefreshLibraryAsync(
        string                        version,
        IReadOnlyList<Icd10CodeEntry> incomingCodes,
        string                        correlationId,
        CancellationToken             ct = default)
    {
        _logger.LogInformation(
            "Icd10LibraryService: starting library refresh. Version={Version} CodeCount={Count} CorrelationId={CorrelationId}",
            version, incomingCodes.Count, correlationId);

        // Build a fast lookup of incoming code values for O(1) membership tests.
        var incomingSet = incomingCodes
            .Select(c => c.CodeValue)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // ── Transaction: all-or-nothing (DR-029) ────────────────────────────
        await using var txn = await _db.Database.BeginTransactionAsync(ct);

        int codesAdded       = 0;
        int codesDeprecated  = 0;

        try
        {
            // ── Step 1: Mark absent-from-incoming codes as deprecated ────────
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            // Process in batches of 500 to avoid large IN-clause parameters.
            var currentCodes = await _db.Set<Icd10CodeLibrary>()
                .Where(l => l.IsCurrent)
                .ToListAsync(ct);

            foreach (var existing in currentCodes)
            {
                if (!incomingSet.Contains(existing.CodeValue))
                {
                    existing.IsCurrent      = false;
                    existing.DeprecatedDate = today;
                    existing.UpdatedAt      = DateTime.UtcNow;
                    codesDeprecated++;
                }
            }

            // ── Step 2: Upsert incoming codes ────────────────────────────────
            var existingVersionedMap = await _db.Set<Icd10CodeLibrary>()
                .Where(l => l.LibraryVersion == version)
                .ToDictionaryAsync(l => l.CodeValue, l => l, StringComparer.OrdinalIgnoreCase, ct);

            foreach (var entry in incomingCodes)
            {
                if (existingVersionedMap.TryGetValue(entry.CodeValue, out var existingRow))
                {
                    // Update in-place (idempotent re-run of same version).
                    existingRow.Description     = entry.Description;
                    existingRow.Category        = entry.Category;
                    existingRow.EffectiveDate   = entry.EffectiveDate;
                    existingRow.DeprecatedDate  = entry.DeprecatedDate;
                    existingRow.ReplacementCode = entry.ReplacementCode;
                    existingRow.IsCurrent       = entry.DeprecatedDate is null;
                    existingRow.UpdatedAt       = DateTime.UtcNow;
                }
                else
                {
                    await _db.Set<Icd10CodeLibrary>().AddAsync(new Icd10CodeLibrary
                    {
                        CodeValue       = entry.CodeValue,
                        Description     = entry.Description,
                        Category        = entry.Category,
                        EffectiveDate   = entry.EffectiveDate,
                        DeprecatedDate  = entry.DeprecatedDate,
                        ReplacementCode = entry.ReplacementCode,
                        LibraryVersion  = version,
                        IsCurrent       = entry.DeprecatedDate is null,
                    }, ct);
                    codesAdded++;
                }
            }

            await _db.SaveChangesAsync(ct);

            // ── Step 3: Revalidate pending MedicalCode records ───────────────
            var revalidation = await RevalidateCoreAsync(correlationId, ct);

            await txn.CommitAsync(ct);

            var result = new LibraryRefreshResult
            {
                Version                  = version,
                CodesAdded               = codesAdded,
                CodesDeprecated          = codesDeprecated,
                PendingCodesRevalidated  = revalidation.TotalExamined,
                DeprecatedRecordsFlagged = revalidation.MarkedDeprecated,
                RefreshedAt              = DateTime.UtcNow,
            };

            _logger.LogInformation(
                "Icd10LibraryService: library refresh committed. " +
                "Version={Version} Added={Added} Deprecated={Deprecated} " +
                "Revalidated={Revalidated} Flagged={Flagged} CorrelationId={CorrelationId}",
                version, codesAdded, codesDeprecated,
                revalidation.TotalExamined, revalidation.MarkedDeprecated, correlationId);

            return result;
        }
        catch (Exception ex)
        {
            await txn.RollbackAsync(ct);
            _logger.LogError(ex,
                "Icd10LibraryService: library refresh failed and was rolled back. " +
                "Version={Version} CorrelationId={CorrelationId}",
                version, correlationId);
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IIcd10LibraryService — RevalidatePendingCodesAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<RevalidationResult> RevalidatePendingCodesAsync(
        string            correlationId,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Icd10LibraryService: starting standalone revalidation. CorrelationId={CorrelationId}",
            correlationId);

        var result = await RevalidateCoreAsync(correlationId, ct);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Icd10LibraryService: revalidation complete. " +
            "Examined={Examined} Valid={Valid} Deprecated={Deprecated} Pending={Pending} " +
            "CorrelationId={CorrelationId}",
            result.TotalExamined, result.MarkedValid,
            result.MarkedDeprecated, result.MarkedPendingReview, correlationId);

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Core revalidation logic (shared by refresh and standalone revalidation)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<RevalidationResult> RevalidateCoreAsync(
        string            correlationId,
        CancellationToken ct)
    {
        // Load pending (unapproved) ICD-10 codes — exclude UNCODABLE sentinels.
        var pendingCodes = await _db.Set<MedicalCode>()
            .Where(m => m.CodeType          == CodeType.Icd10
                     && m.ApprovedByUserId  == null
                     && m.CodeValue         != "UNCODABLE")
            .ToListAsync(ct);

        if (pendingCodes.Count == 0)
            return new RevalidationResult { RevalidatedAt = DateTime.UtcNow };

        // Build current library lookup: codeValue → is_current + library version.
        var allLibEntries = await _db.Set<Icd10CodeLibrary>()
            .AsNoTracking()
            .ToListAsync(ct);

        // Group by code value; prefer the most-recent (highest version) entry.
        var libraryLookup = allLibEntries
            .GroupBy(l => l.CodeValue, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(e => e.LibraryVersion, StringComparer.Ordinal).First(),
                StringComparer.OrdinalIgnoreCase);

        int markedValid       = 0;
        int markedDeprecated  = 0;
        int markedPendingReview = 0;

        foreach (var code in pendingCodes)
        {
            if (!libraryLookup.TryGetValue(code.CodeValue, out var libEntry))
            {
                // Code not found in any library version — flag for manual review.
                code.RevalidationStatus = RevalidationStatus.PendingReview;
                code.UpdatedAt          = DateTime.UtcNow;
                markedPendingReview++;
            }
            else if (!libEntry.IsCurrent)
            {
                // Code is in the library but deprecated — flag and capture version (edge case).
                code.RevalidationStatus = RevalidationStatus.DeprecatedReplaced;
                code.LibraryVersion     = libEntry.LibraryVersion;
                code.UpdatedAt          = DateTime.UtcNow;
                markedDeprecated++;
            }
            else
            {
                // Code is current — mark valid and capture version.
                code.RevalidationStatus = RevalidationStatus.Valid;
                code.LibraryVersion     = libEntry.LibraryVersion;
                code.UpdatedAt          = DateTime.UtcNow;
                markedValid++;
            }
        }

        return new RevalidationResult
        {
            TotalExamined     = pendingCodes.Count,
            MarkedValid       = markedValid,
            MarkedDeprecated  = markedDeprecated,
            MarkedPendingReview = markedPendingReview,
            RevalidatedAt     = DateTime.UtcNow,
        };
    }
}
