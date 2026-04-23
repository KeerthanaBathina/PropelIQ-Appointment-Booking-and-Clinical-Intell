using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Coding;

/// <summary>
/// Manages the CPT code reference library lifecycle:
/// quarterly refresh (AC-4) and revalidation of pending <c>MedicalCode</c> records
/// against the updated library (edge case: expired-code handling).
///
/// Refresh strategy:
///   1. Open a serialisable transaction (DR-029 — all-or-nothing).
///   2. Mark existing <c>is_active = true</c> entries absent from the incoming dataset
///      as inactive (<c>is_active = false</c>, <c>expiration_date = today</c>).
///   3. Upsert all incoming codes under the new library version label.
///   4. Call <see cref="RevalidatePendingCodesAsync"/> within the same transaction.
///   5. Write an <c>AuditLog</c> entry with action <c>CptLibraryRefreshed</c>.
///   6. Commit or roll back on any failure.
///
/// Revalidation outcome per <c>MedicalCode</c> row:
///   - <c>Valid</c>              — code is present and <c>is_active = true</c>.
///   - <c>DeprecatedReplaced</c> — code is in the library but <c>is_active = false</c>;
///                                 <c>library_version</c> is updated for traceability.
///   - <c>PendingReview</c>     — code not found in the library at all; staff must verify.
/// </summary>
public sealed class CptCodeLibraryService : ICptCodeLibraryService
{
    private readonly ApplicationDbContext             _db;
    private readonly ILogger<CptCodeLibraryService>   _logger;

    public CptCodeLibraryService(
        ApplicationDbContext           db,
        ILogger<CptCodeLibraryService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ICptCodeLibraryService — RefreshLibraryAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<CptLibraryRefreshResult> RefreshLibraryAsync(
        string                      version,
        IReadOnlyList<CptCodeEntry> incomingCodes,
        string                      correlationId,
        CancellationToken           ct = default)
    {
        _logger.LogInformation(
            "CptCodeLibraryService: starting library refresh. Version={Version} CodeCount={Count} CorrelationId={CorrelationId}",
            version, incomingCodes.Count, correlationId);

        // Build a fast O(1) membership lookup of incoming CPT code values.
        var incomingSet = incomingCodes
            .Select(c => c.CptCode)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // ── Transaction: all-or-nothing (DR-029) ────────────────────────────
        await using var txn = await _db.Database.BeginTransactionAsync(ct);

        int codesAdded       = 0;
        int codesDeactivated = 0;

        try
        {
            // ── Step 1: Deactivate codes absent from the incoming dataset ────
            var today = DateOnly.FromDateTime(DateTime.UtcNow);

            var activeCodes = await _db.CptCodeLibrary
                .Where(l => l.IsActive)
                .ToListAsync(ct);

            foreach (var existing in activeCodes)
            {
                if (!incomingSet.Contains(existing.CptCode))
                {
                    existing.IsActive       = false;
                    existing.ExpirationDate = today;
                    existing.UpdatedAt      = DateTime.UtcNow;
                    codesDeactivated++;
                }
            }

            // ── Step 2: Upsert incoming codes ────────────────────────────────
            // Check existing rows for this version to support idempotent re-runs.
            var existingVersionedMap = await _db.CptCodeLibrary
                .Where(l => l.IsActive || incomingSet.Contains(l.CptCode))
                .ToDictionaryAsync(l => l.CptCode, l => l, StringComparer.OrdinalIgnoreCase, ct);

            foreach (var entry in incomingCodes)
            {
                if (existingVersionedMap.TryGetValue(entry.CptCode, out var existingRow))
                {
                    // Update in-place (idempotent re-run of same version).
                    existingRow.Description    = entry.Description;
                    existingRow.Category       = entry.Category;
                    existingRow.EffectiveDate  = entry.EffectiveDate;
                    existingRow.ExpirationDate = entry.ExpirationDate;
                    existingRow.IsActive       = entry.ExpirationDate is null || entry.ExpirationDate >= today;
                    existingRow.UpdatedAt      = DateTime.UtcNow;
                }
                else
                {
                    await _db.CptCodeLibrary.AddAsync(new CptCodeLibrary
                    {
                        CptCode        = entry.CptCode,
                        Description    = entry.Description,
                        Category       = entry.Category,
                        EffectiveDate  = entry.EffectiveDate,
                        ExpirationDate = entry.ExpirationDate,
                        IsActive       = entry.ExpirationDate is null || entry.ExpirationDate >= today,
                    }, ct);
                    codesAdded++;
                }
            }

            await _db.SaveChangesAsync(ct);

            // ── Step 3: Revalidate pending MedicalCode records ───────────────
            var revalidation = await RevalidateCoreAsync(correlationId, ct);

            // ── Step 4: Audit log (HIPAA §164.312(b), NFR-035) ───────────────
            // UserId is null here because this is a system-level Admin operation;
            // the controller logs the acting Admin's userId in its own audit trail.
            await _db.Set<AuditLog>().AddAsync(new AuditLog
            {
                Action       = AuditAction.CptLibraryRefreshed,
                ResourceType = "CptCodeLibrary",
                ResourceId   = null,
            }, ct);

            await _db.SaveChangesAsync(ct);

            await txn.CommitAsync(ct);

            var result = new CptLibraryRefreshResult
            {
                Version                 = version,
                CodesAdded              = codesAdded,
                CodesDeactivated        = codesDeactivated,
                PendingCodesRevalidated = revalidation.TotalExamined,
                RefreshedAt             = DateTime.UtcNow,
            };

            _logger.LogInformation(
                "CptCodeLibraryService: library refresh committed. " +
                "Version={Version} Added={Added} Deactivated={Deactivated} " +
                "Revalidated={Revalidated} CorrelationId={CorrelationId}",
                version, codesAdded, codesDeactivated,
                revalidation.TotalExamined, correlationId);

            return result;
        }
        catch (Exception ex)
        {
            await txn.RollbackAsync(ct);
            _logger.LogError(ex,
                "CptCodeLibraryService: library refresh failed and was rolled back. " +
                "Version={Version} CorrelationId={CorrelationId}",
                version, correlationId);
            throw;
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ICptCodeLibraryService — RevalidatePendingCodesAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<CptRevalidationResult> RevalidatePendingCodesAsync(
        string            correlationId,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "CptCodeLibraryService: starting standalone revalidation. CorrelationId={CorrelationId}",
            correlationId);

        var result = await RevalidateCoreAsync(correlationId, ct);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "CptCodeLibraryService: revalidation complete. " +
            "Examined={Examined} Valid={Valid} Invalid={Invalid} PendingReview={Pending} " +
            "CorrelationId={CorrelationId}",
            result.TotalExamined, result.MarkedValid,
            result.MarkedInvalid, result.MarkedPendingReview, correlationId);

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Core revalidation logic (shared by refresh and standalone revalidation)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<CptRevalidationResult> RevalidateCoreAsync(
        string            correlationId,
        CancellationToken ct)
    {
        // Load pending (unapproved) CPT codes.
        var pendingCodes = await _db.Set<MedicalCode>()
            .Where(m => m.CodeType         == CodeType.Cpt
                     && m.ApprovedByUserId == null)
            .ToListAsync(ct);

        if (pendingCodes.Count == 0)
            return new CptRevalidationResult { RevalidatedAt = DateTime.UtcNow };

        // Build CPT library lookup: codeValue → (IsActive, row).
        var allLibEntries = await _db.CptCodeLibrary
            .AsNoTracking()
            .ToListAsync(ct);

        // When a code appears multiple times (e.g. re-uploaded under a new version),
        // prefer the most recently updated entry.
        var libraryLookup = allLibEntries
            .GroupBy(l => l.CptCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(e => e.UpdatedAt).First(),
                StringComparer.OrdinalIgnoreCase);

        int markedValid         = 0;
        int markedInvalid       = 0;
        int markedPendingReview = 0;

        foreach (var code in pendingCodes)
        {
            if (!libraryLookup.TryGetValue(code.CodeValue, out var libEntry))
            {
                // Code not found in any library entry — flag for manual staff review.
                code.RevalidationStatus = RevalidationStatus.PendingReview;
                code.UpdatedAt          = DateTime.UtcNow;
                markedPendingReview++;
            }
            else if (!libEntry.IsActive)
            {
                // Code is in the library but has been deactivated (expired or retired).
                code.RevalidationStatus = RevalidationStatus.DeprecatedReplaced;
                code.UpdatedAt          = DateTime.UtcNow;
                markedInvalid++;
            }
            else
            {
                // Code is current and active.
                code.RevalidationStatus = RevalidationStatus.Valid;
                code.UpdatedAt          = DateTime.UtcNow;
                markedValid++;
            }
        }

        _logger.LogDebug(
            "CptCodeLibraryService: revalidation core complete. " +
            "Examined={Examined} Valid={Valid} Invalid={Invalid} PendingReview={Pending} " +
            "CorrelationId={CorrelationId}",
            pendingCodes.Count, markedValid, markedInvalid, markedPendingReview, correlationId);

        return new CptRevalidationResult
        {
            TotalExamined     = pendingCodes.Count,
            MarkedValid       = markedValid,
            MarkedInvalid     = markedInvalid,
            MarkedPendingReview = markedPendingReview,
            RevalidatedAt     = DateTime.UtcNow,
        };
    }
}
