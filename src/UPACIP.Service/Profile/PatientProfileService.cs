using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Caching;
using UPACIP.Service.Consolidation;

namespace UPACIP.Service.Profile;

/// <summary>
/// Patient profile aggregation service (US_043, AC-1, AC-2, AC-3, FR-052, FR-056).
///
/// Aggregates extracted clinical data from PostgreSQL using EF Core projections,
/// applies a 5-minute Redis cache-aside pattern (NFR-030), and delegates manual
/// consolidation triggers to <see cref="IConsolidationService"/>.
///
/// Cache keys:
///   - <c>upacip:profile:{patientId}</c>         — full 360° profile (5-min TTL)
///   - <c>upacip:profile:{patientId}:versions</c> — version history list (5-min TTL)
///
/// Both keys are invalidated when <see cref="TriggerConsolidationAsync"/> completes
/// so the UI always reflects the freshly consolidated state on the next request.
/// </summary>
public sealed class PatientProfileService : IPatientProfileService
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5); // NFR-030

    private static string ProfileCacheKey(Guid patientId)   => $"upacip:profile:{patientId}";
    private static string VersionsCacheKey(Guid patientId)  => $"upacip:profile:{patientId}:versions";

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ApplicationDbContext            _db;
    private readonly ICacheService                   _cache;
    private readonly IConsolidationService           _consolidation;
    private readonly ILogger<PatientProfileService>  _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public PatientProfileService(
        ApplicationDbContext           db,
        ICacheService                  cache,
        IConsolidationService          consolidation,
        ILogger<PatientProfileService> logger)
    {
        _db            = db;
        _cache         = cache;
        _consolidation = consolidation;
        _logger        = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IPatientProfileService — GetProfile360Async
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<PatientProfile360Dto?> GetProfile360Async(Guid patientId, CancellationToken ct = default)
        => _cache.GetOrSetAsync(
            ProfileCacheKey(patientId),
            factory: () => BuildProfile360Async(patientId, ct),
            expiration: CacheTtl,
            cancellationToken: ct);

    // ─────────────────────────────────────────────────────────────────────────
    // IPatientProfileService — GetVersionHistoryAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<VersionHistoryDto>> GetVersionHistoryAsync(Guid patientId, CancellationToken ct = default)
    {
        var cached = await _cache.GetAsync<List<VersionHistoryDto>>(VersionsCacheKey(patientId), ct);
        if (cached is not null) return cached;

        var versions = await BuildVersionHistoryAsync(patientId, ct);
        await _cache.SetAsync(VersionsCacheKey(patientId), versions, CacheTtl, ct);
        return versions;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IPatientProfileService — GetVersionSnapshotAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<VersionHistoryDto?> GetVersionSnapshotAsync(Guid patientId, int versionNumber, CancellationToken ct = default)
    {
        var version = await _db.PatientProfileVersions
            .AsNoTracking()
            .Where(v => v.PatientId == patientId && v.VersionNumber == versionNumber)
            .Select(v => new
            {
                v.VersionNumber,
                v.CreatedAt,
                v.ConsolidatedByUserId,
                v.ConsolidationType,
                v.SourceDocumentIds,
                v.DataSnapshot,
            })
            .FirstOrDefaultAsync(ct);

        if (version is null) return null;

        string consolidatedByName = "Automated";
        if (version.ConsolidatedByUserId.HasValue)
        {
            consolidatedByName = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == version.ConsolidatedByUserId.Value)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync(ct) ?? "Unknown";
        }

        return new VersionHistoryDto
        {
            VersionNumber          = version.VersionNumber,
            CreatedAt              = version.CreatedAt,
            ConsolidatedByUserName = consolidatedByName,
            ConsolidationType      = version.ConsolidationType.ToString(),
            SourceDocumentCount    = version.SourceDocumentIds.Count,
            DataSnapshot           = version.DataSnapshot,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IPatientProfileService — GetSourceCitationAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<SourceCitationDto?> GetSourceCitationAsync(
        Guid patientId,
        Guid extractedDataId,
        CancellationToken ct = default)
    {
        // Join through ClinicalDocument to validate patient ownership (OWASP A01 — Broken Access Control).
        var row = await _db.ExtractedData
            .AsNoTracking()
            .Include(e => e.Document)
            .Where(e => e.Id == extractedDataId
                     && e.Document.PatientId == patientId)
            .Select(e => new
            {
                e.Id,
                e.DocumentId,
                DocumentName     = e.Document.OriginalFileName,
                DocumentCategory = e.Document.DocumentCategory,
                UploadDate       = e.Document.UploadDate,
                e.PageNumber,
                e.ExtractionRegion,
                SourceSnippet    = e.DataContent != null ? e.DataContent.SourceSnippet : null,
                e.SourceAttribution,
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return null;

        return new SourceCitationDto
        {
            ExtractedDataId  = row.Id,
            DocumentId       = row.DocumentId,
            DocumentName     = row.DocumentName,
            DocumentCategory = row.DocumentCategory,
            UploadDate       = row.UploadDate,
            PageNumber       = row.PageNumber,
            ExtractionRegion = row.ExtractionRegion,
            SourceSnippet    = row.SourceSnippet,
            SourceAttribution = row.SourceAttribution,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IPatientProfileService — TriggerConsolidationAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ConsolidationResult> TriggerConsolidationAsync(
        Guid              patientId,
        Guid              triggeredByUserId,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "PatientProfileService: manual consolidation triggered. PatientId={PatientId}, TriggeredBy={UserId}",
            patientId, triggeredByUserId);

        var result = await _consolidation.ConsolidatePatientProfileAsync(patientId, triggeredByUserId, ct);

        // Invalidate profile and version-history cache so the next read reflects the new version.
        await Task.WhenAll(
            _cache.RemoveAsync(ProfileCacheKey(patientId),  ct),
            _cache.RemoveAsync(VersionsCacheKey(patientId), ct));

        _logger.LogInformation(
            "PatientProfileService: cache invalidated after consolidation. PatientId={PatientId}, Version={Version}",
            patientId, result.NewVersionNumber);

        return result;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the full 360° profile DTO from the database.
    /// Called by the cache-aside pattern in <see cref="GetProfile360Async"/>.
    /// </summary>
    private async Task<PatientProfile360Dto?> BuildProfile360Async(Guid patientId, CancellationToken ct)
    {
        // ── 1. Patient summary ──────────────────────────────────────────────
        var patient = await _db.Patients
            .AsNoTracking()
            .Where(p => p.Id == patientId && p.DeletedAt == null)
            .Select(p => new { p.Id, p.FullName, p.DateOfBirth })
            .FirstOrDefaultAsync(ct);

        if (patient is null) return null;

        // ── 2. Current version metadata ─────────────────────────────────────
        var latestVersion = await _db.PatientProfileVersions
            .AsNoTracking()
            .Where(v => v.PatientId == patientId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new { v.VersionNumber, v.CreatedAt })
            .FirstOrDefaultAsync(ct);

        // ── 3. Extracted data — all non-archived entries with document join ─
        var extractedRows = await _db.ExtractedData
            .AsNoTracking()
            .Include(e => e.Document)
            .Where(e => e.Document.PatientId == patientId
                     && !e.IsArchived
                     && e.Document.ProcessingStatus == ProcessingStatus.Completed)
            .OrderByDescending(e => e.ConfidenceScore)
            .Select(e => new
            {
                e.Id,
                e.DataType,
                NormalizedValue      = e.DataContent != null ? e.DataContent.NormalizedValue : null,
                RawText              = e.DataContent != null ? e.DataContent.RawText : null,
                Unit                 = e.DataContent != null ? e.DataContent.Unit : null,
                SourceSnippet        = e.DataContent != null ? e.DataContent.SourceSnippet : null,
                e.ConfidenceScore,
                e.DocumentId,
                DocumentName         = e.Document.OriginalFileName,
                DocumentCategory     = e.Document.DocumentCategory,
                e.PageNumber,
                e.ExtractionRegion,
                e.SourceAttribution,
                e.FlaggedForReview,
                e.VerificationStatus,
                e.VerifiedAtUtc,
            })
            .ToListAsync(ct);

        // ── 4. Map to DTOs grouped by DataType ──────────────────────────────
        ProfileDataPointDto ToDto(dynamic row) => new()
        {
            ExtractedDataId      = row.Id,
            DataType             = row.DataType,
            NormalizedValue      = row.NormalizedValue,
            RawText              = row.RawText,
            Unit                 = row.Unit,
            SourceSnippet        = row.SourceSnippet,
            ConfidenceScore      = row.ConfidenceScore,
            SourceDocumentId     = row.DocumentId,
            SourceDocumentName   = row.DocumentName,
            SourceDocumentCategory = row.DocumentCategory,
            PageNumber           = row.PageNumber,
            ExtractionRegion     = row.ExtractionRegion,
            SourceAttribution    = row.SourceAttribution,
            FlaggedForReview     = row.FlaggedForReview,
            VerificationStatus   = row.VerificationStatus.ToString(),
            VerifiedAtUtc        = row.VerifiedAtUtc,
        };

        var medications = extractedRows.Where(r => r.DataType == DataType.Medication).Select(ToDto).ToList();
        var diagnoses   = extractedRows.Where(r => r.DataType == DataType.Diagnosis).Select(ToDto).ToList();
        var procedures  = extractedRows.Where(r => r.DataType == DataType.Procedure).Select(ToDto).ToList();
        var allergies   = extractedRows.Where(r => r.DataType == DataType.Allergy).Select(ToDto).ToList();

        return new PatientProfile360Dto
        {
            PatientId             = patient.Id,
            PatientName           = patient.FullName,
            DateOfBirth           = patient.DateOfBirth,
            CurrentVersionNumber  = latestVersion?.VersionNumber ?? 0,
            LastConsolidatedAt    = latestVersion?.CreatedAt,
            PendingReviewCount    = extractedRows.Count(r => r.FlaggedForReview),
            ConflictCount         = 0, // Populated by IConflictDetectionService (US_043 task_004)
            Medications           = medications,
            Diagnoses             = diagnoses,
            Procedures            = procedures,
            Allergies             = allergies,
        };
    }

    /// <summary>
    /// Builds the complete version history list from the database.
    /// Called by the cache-aside pattern in <see cref="GetVersionHistoryAsync"/>.
    /// </summary>
    private async Task<List<VersionHistoryDto>> BuildVersionHistoryAsync(Guid patientId, CancellationToken ct)
    {
        var versions = await _db.PatientProfileVersions
            .AsNoTracking()
            .Where(v => v.PatientId == patientId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new
            {
                v.VersionNumber,
                v.CreatedAt,
                v.ConsolidatedByUserId,
                v.ConsolidationType,
                v.SourceDocumentIds,
                v.DataSnapshot,
            })
            .ToListAsync(ct);

        if (versions.Count == 0) return [];

        // Batch-resolve user names for all versions that have a non-null ConsolidatedByUserId.
        var userIds = versions
            .Where(v => v.ConsolidatedByUserId.HasValue)
            .Select(v => v.ConsolidatedByUserId!.Value)
            .Distinct()
            .ToList();

        var userNames = userIds.Count > 0
            ? await _db.Users
                .AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName })
                .ToDictionaryAsync(u => u.Id, u => u.FullName, ct)
            : new Dictionary<Guid, string>();

        return versions.Select(v => new VersionHistoryDto
        {
            VersionNumber          = v.VersionNumber,
            CreatedAt              = v.CreatedAt,
            ConsolidatedByUserName = v.ConsolidatedByUserId.HasValue
                                        ? userNames.GetValueOrDefault(v.ConsolidatedByUserId.Value, "Unknown")
                                        : "Automated",
            ConsolidationType      = v.ConsolidationType.ToString(),
            SourceDocumentCount    = v.SourceDocumentIds.Count,
            DataSnapshot           = v.DataSnapshot,
        }).ToList();
    }
}
