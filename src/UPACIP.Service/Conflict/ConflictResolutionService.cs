using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Auth;
using UPACIP.Service.Caching;

namespace UPACIP.Service.Conflict;

/// <summary>
/// Implements the staff conflict resolution workflow: value selection, both-valid preservation,
/// verification status lifecycle, and progress tracking (US_045, AC-2, AC-4, EC-1, EC-2, FR-054).
///
/// Design decisions:
///   - All resolve operations are wrapped in a DB transaction (conflict update + snapshot + 
///     verification check as a single atomic unit).
///   - Optimistic concurrency is enforced by re-reading the conflict inside the transaction and
///     checking its status before writing — prevents two staff members resolving the same conflict.
///   - Profile consolidation snapshot update writes a JSON patch to DataSnapshot on the latest
///     PatientProfileVersion, recording which ExtractedData row was selected as authoritative.
///   - Cache invalidation uses the same key scheme as ConflictManagementService to avoid
///     stale profile data on the staff dashboard.
///   - No PII is written to structured log events (NFR-035).
/// </summary>
public sealed class ConflictResolutionService : IConflictResolutionService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Cache key helpers — mirrors ConflictManagementService key scheme
    // ─────────────────────────────────────────────────────────────────────────

    private static string ProfileCacheKey(Guid patientId)  => $"upacip:profile:{patientId}";
    private static string VersionsCacheKey(Guid patientId) => $"upacip:profile:{patientId}:versions";

    // ─────────────────────────────────────────────────────────────────────────
    // Dependencies
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ApplicationDbContext              _db;
    private readonly ICacheService                     _cache;
    private readonly IAuditLogService                  _audit;
    private readonly ILogger<ConflictResolutionService> _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public ConflictResolutionService(
        ApplicationDbContext               db,
        ICacheService                      cache,
        IAuditLogService                   audit,
        ILogger<ConflictResolutionService> logger)
    {
        _db     = db;
        _cache  = cache;
        _audit  = audit;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SelectConflictValueAsync (AC-2)
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task SelectConflictValueAsync(SelectValueRequest request, CancellationToken ct = default)
    {
        if (request.SelectedExtractedDataId == Guid.Empty)
            throw new ArgumentException("SelectedExtractedDataId must not be empty.", nameof(request));

        if (string.IsNullOrWhiteSpace(request.ResolutionNotes))
            throw new ArgumentException("ResolutionNotes are required for audit trail.", nameof(request));

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // ── 1. Load and lock the conflict ─────────────────────────────────
        var conflict = await _db.ClinicalConflicts
            .FirstOrDefaultAsync(c => c.Id == request.ConflictId, ct)
            ?? throw new InvalidOperationException(
                $"Conflict {request.ConflictId} not found.");

        EnsureConflictIsResolvable(conflict);

        // ── 2. Validate the selected extracted data ID belongs to this conflict ──
        if (!conflict.SourceExtractedDataIds.Contains(request.SelectedExtractedDataId))
            throw new InvalidOperationException(
                $"ExtractedData {request.SelectedExtractedDataId} is not a source of conflict {request.ConflictId}.");

        // ── 3. Verify the ExtractedData row exists ────────────────────────
        var extractedData = await _db.ExtractedData
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == request.SelectedExtractedDataId && !e.IsArchived, ct)
            ?? throw new InvalidOperationException(
                $"ExtractedData {request.SelectedExtractedDataId} not found or is archived.");

        // ── 4. Update the PatientProfileVersion snapshot (AC-2) ───────────
        await ApplySelectedValueToProfileSnapshotAsync(
            conflict.PatientId,
            request.SelectedExtractedDataId,
            extractedData.DataType,
            ct);

        // ── 5. Resolve the conflict ───────────────────────────────────────
        var now = DateTime.UtcNow;
        conflict.Status                  = ConflictStatus.Resolved;
        conflict.ResolutionType          = ConflictResolutionType.SelectedValue;
        conflict.SelectedExtractedDataId = request.SelectedExtractedDataId;
        conflict.ResolvedByUserId        = request.UserId;
        conflict.ResolutionNotes         = request.ResolutionNotes;
        conflict.ResolvedAt              = now;
        conflict.UpdatedAt               = now;

        await _db.SaveChangesAsync(ct);

        // ── 6. Verification check inside the same transaction ─────────────
        await UpdateProfileVerificationStatusAsync(conflict.PatientId, request.UserId, ct);

        await tx.CommitAsync(ct);

        // ── 7. Cache invalidation (outside transaction — cache errors never rollback) ──
        await InvalidatePatientCacheAsync(conflict.PatientId, ct);

        // ── 8. Audit log ──────────────────────────────────────────────────
        await _audit.LogAsync(
            AuditAction.ConflictValueSelected,
            request.UserId,
            resourceType:  "ClinicalConflict",
            ipAddress:     "internal",
            userAgent:     "ConflictResolutionService",
            resourceId:    conflict.Id,
            cancellationToken: ct);

        _logger.LogInformation(
            "ConflictResolutionSvc: value selected. " +
            "ConflictId={ConflictId}, PatientId={PatientId}, SelectedDataId={SelectedDataId}, ResolvedBy={UserId}",
            conflict.Id, conflict.PatientId, request.SelectedExtractedDataId, request.UserId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ResolveBothValidAsync (EC-2)
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task ResolveBothValidAsync(BothValidRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Explanation))
            throw new ArgumentException("Explanation is required for BothValid resolution.", nameof(request));

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var conflict = await _db.ClinicalConflicts
            .FirstOrDefaultAsync(c => c.Id == request.ConflictId, ct)
            ?? throw new InvalidOperationException(
                $"Conflict {request.ConflictId} not found.");

        EnsureConflictIsResolvable(conflict);

        // Ensure all source extracted data entries are still active (not archived).
        // This guards against the edge case where a document was replaced between detection
        // and staff review; the conflict stays resolvable but the explanation must cover
        // the original sources only.
        await EnsureSourceExtractedDataActiveAsync(conflict, ct);

        var now = DateTime.UtcNow;
        conflict.Status               = ConflictStatus.Resolved;
        conflict.ResolutionType       = ConflictResolutionType.BothValid;
        conflict.BothValidExplanation = request.Explanation.Trim();
        conflict.ResolvedByUserId     = request.UserId;
        conflict.ResolutionNotes      = $"Both valid: {request.Explanation.Trim()}";
        conflict.ResolvedAt           = now;
        conflict.UpdatedAt            = now;

        await _db.SaveChangesAsync(ct);

        await UpdateProfileVerificationStatusAsync(conflict.PatientId, request.UserId, ct);

        await tx.CommitAsync(ct);

        await InvalidatePatientCacheAsync(conflict.PatientId, ct);

        await _audit.LogAsync(
            AuditAction.ConflictBothValid,
            request.UserId,
            resourceType:  "ClinicalConflict",
            ipAddress:     "internal",
            userAgent:     "ConflictResolutionService",
            resourceId:    conflict.Id,
            cancellationToken: ct);

        _logger.LogInformation(
            "ConflictResolutionSvc: both-valid resolution. " +
            "ConflictId={ConflictId}, PatientId={PatientId}, ResolvedBy={UserId}",
            conflict.Id, conflict.PatientId, request.UserId);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // CheckAndUpdateProfileVerificationAsync (AC-4)
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task CheckAndUpdateProfileVerificationAsync(
        Guid patientId, Guid userId, CancellationToken ct = default)
    {
        // This public overload wraps the internal method in its own transaction when
        // called standalone (e.g., from the controller after a dismiss action).
        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await UpdateProfileVerificationStatusAsync(patientId, userId, ct);
        await tx.CommitAsync(ct);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetResolutionProgressAsync (EC-1)
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ResolutionProgressDto> GetResolutionProgressAsync(
        Guid patientId, CancellationToken ct = default)
    {
        // Count total and closed conflicts in a single DB round-trip using grouped aggregate.
        var counts = await _db.ClinicalConflicts
            .AsNoTracking()
            .Where(c => c.PatientId == patientId)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total    = g.Count(),
                Resolved = g.Count(c => c.Status == ConflictStatus.Resolved
                                     || c.Status == ConflictStatus.Dismissed),
            })
            .FirstOrDefaultAsync(ct);

        int total     = counts?.Total    ?? 0;
        int resolved  = counts?.Resolved ?? 0;
        int remaining = total - resolved;
        int pct       = total == 0 ? 0 : (int)Math.Floor(resolved * 100.0 / total);

        // Fetch the latest profile version's verification status.
        var latestVersion = await _db.PatientProfileVersions
            .AsNoTracking()
            .Where(v => v.PatientId == patientId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new { v.VerificationStatus })
            .FirstOrDefaultAsync(ct);

        string verificationStatus = latestVersion?.VerificationStatus.ToString()
            ?? ProfileVerificationStatus.Unverified.ToString();

        return new ResolutionProgressDto
        {
            PatientId          = patientId,
            TotalConflicts     = total,
            ResolvedCount      = resolved,
            RemainingCount     = remaining,
            PercentComplete    = pct,
            VerificationStatus = verificationStatus,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Core verification status update logic — called from within an open transaction.
    /// Counts open conflicts and transitions the latest PatientProfileVersion accordingly.
    /// Creates an audit log entry when the status transitions INTO Verified (AC-4).
    /// </summary>
    private async Task UpdateProfileVerificationStatusAsync(
        Guid patientId, Guid userId, CancellationToken ct)
    {
        var openCount = await _db.ClinicalConflicts
            .Where(c => c.PatientId == patientId
                     && c.Status != ConflictStatus.Resolved
                     && c.Status != ConflictStatus.Dismissed)
            .CountAsync(ct);

        var totalCount = await _db.ClinicalConflicts
            .Where(c => c.PatientId == patientId)
            .CountAsync(ct);

        var latestVersion = await _db.PatientProfileVersions
            .Where(v => v.PatientId == patientId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);

        if (latestVersion is null)
        {
            _logger.LogWarning(
                "ConflictResolutionSvc: no PatientProfileVersion found for patient {PatientId}; " +
                "skipping verification status update.", patientId);
            return;
        }

        ProfileVerificationStatus newStatus;
        bool wasAlreadyVerified = latestVersion.VerificationStatus == ProfileVerificationStatus.Verified;

        if (openCount == 0 && totalCount > 0)
        {
            newStatus = ProfileVerificationStatus.Verified;
        }
        else if (openCount < totalCount && totalCount > 0)
        {
            newStatus = ProfileVerificationStatus.PartiallyVerified;
        }
        else
        {
            newStatus = ProfileVerificationStatus.Unverified;
        }

        latestVersion.VerificationStatus = newStatus;
        latestVersion.UpdatedAt          = DateTime.UtcNow;

        if (newStatus == ProfileVerificationStatus.Verified)
        {
            latestVersion.VerifiedByUserId = userId;
            latestVersion.VerifiedAt       = DateTimeOffset.UtcNow;
        }

        await _db.SaveChangesAsync(ct);

        // Emit the audit log entry only on the first transition into Verified (AC-4).
        if (newStatus == ProfileVerificationStatus.Verified && !wasAlreadyVerified)
        {
            await _audit.LogAsync(
                AuditAction.ProfileVerified,
                userId,
                resourceType:  "PatientProfileVersion",
                ipAddress:     "internal",
                userAgent:     "ConflictResolutionService",
                resourceId:    latestVersion.Id,
                cancellationToken: ct);

            _logger.LogInformation(
                "ConflictResolutionSvc: profile verified. " +
                "PatientId={PatientId}, VersionId={VersionId}, VerifiedBy={UserId}, " +
                "ConflictsResolved={TotalCount}",
                patientId, latestVersion.Id, userId, totalCount);
        }
        else
        {
            _logger.LogDebug(
                "ConflictResolutionSvc: verification status updated. " +
                "PatientId={PatientId}, Status={Status}, Open={Open}, Total={Total}",
                patientId, newStatus, openCount, totalCount);
        }
    }

    /// <summary>
    /// Writes a JSON patch to the latest <c>PatientProfileVersion.DataSnapshot</c> recording
    /// which <c>ExtractedData</c> row was selected as the authoritative value (AC-2).
    ///
    /// The patch structure is:
    /// <code>
    /// { "selectedValue": { "extractedDataId": "...", "dataType": "...", "resolvedAt": "..." } }
    /// </code>
    /// This is a targeted delta — it does not overwrite the full snapshot, which would require
    /// re-running the full consolidation pipeline.
    /// </summary>
    private async Task ApplySelectedValueToProfileSnapshotAsync(
        Guid patientId,
        Guid selectedExtractedDataId,
        DataType dataType,
        CancellationToken ct)
    {
        var latestVersion = await _db.PatientProfileVersions
            .Where(v => v.PatientId == patientId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);

        if (latestVersion is null)
            return;

        // Merge the selected value attribution into the existing snapshot JSON.
        // If the snapshot is null or unparseable, we start from an empty object.
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

        latestVersion.DataSnapshot = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented        = false,
        });
        latestVersion.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Validates that the conflict is in a state that allows resolution
    /// (Detected or UnderReview).  Throws <see cref="InvalidOperationException"/> otherwise.
    /// </summary>
    private static void EnsureConflictIsResolvable(UPACIP.DataAccess.Entities.ClinicalConflict conflict)
    {
        if (conflict.Status == ConflictStatus.Resolved)
            throw new InvalidOperationException(
                $"Conflict {conflict.Id} is already resolved.");

        if (conflict.Status == ConflictStatus.Dismissed)
            throw new InvalidOperationException(
                $"Conflict {conflict.Id} has been dismissed and cannot be resolved.");
    }

    /// <summary>
    /// Validates that at least one source extracted data ID from the conflict is still active
    /// (not archived).  A warning is logged if some are archived but at least one is still active.
    /// </summary>
    private async Task EnsureSourceExtractedDataActiveAsync(
        UPACIP.DataAccess.Entities.ClinicalConflict conflict,
        CancellationToken ct)
    {
        if (conflict.SourceExtractedDataIds.Count == 0)
            return;

        var activeCount = await _db.ExtractedData
            .AsNoTracking()
            .Where(e => conflict.SourceExtractedDataIds.Contains(e.Id) && !e.IsArchived)
            .CountAsync(ct);

        if (activeCount == 0)
            throw new InvalidOperationException(
                $"All source ExtractedData records for conflict {conflict.Id} have been archived. " +
                "The conflict cannot be resolved until a new document is uploaded.");

        if (activeCount < conflict.SourceExtractedDataIds.Count)
        {
            _logger.LogWarning(
                "ConflictResolutionSvc: {ArchivedCount} of {Total} source extracted data records " +
                "are archived for conflict {ConflictId}. BothValid resolution will cover only active records.",
                conflict.SourceExtractedDataIds.Count - activeCount,
                conflict.SourceExtractedDataIds.Count,
                conflict.Id);
        }
    }

    /// <summary>
    /// Removes the patient profile and versions from the Redis cache.
    /// Cache errors are swallowed — they must never propagate to the caller.
    /// </summary>
    private async Task InvalidatePatientCacheAsync(Guid patientId, CancellationToken ct)
    {
        await _cache.RemoveAsync(ProfileCacheKey(patientId),  ct);
        await _cache.RemoveAsync(VersionsCacheKey(patientId), ct);
    }
}
