using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Auth;
using UPACIP.Service.Caching;

namespace UPACIP.Service.Profile;

/// <summary>
/// Staff-facing patient search with Redis cache-aside (US_062 AC-1, NFR-030).
///
/// Cache keys:
///   <c>patient:search:{queryHash}</c>  — search results,    5-min TTL
///   <c>staff:providers:list</c>        — provider dropdown, 5-min TTL
///
/// Audit: DataAccess entry written per search regardless of cache hit.
/// </summary>
public sealed class PatientSearchService : IPatientSearchService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly ApplicationDbContext          _db;
    private readonly ICacheService                 _cache;
    private readonly IAuditLogService              _audit;
    private readonly ILogger<PatientSearchService> _logger;

    public PatientSearchService(
        ApplicationDbContext          db,
        ICacheService                 cache,
        IAuditLogService              audit,
        ILogger<PatientSearchService> logger)
    {
        _db     = db;
        _cache  = cache;
        _audit  = audit;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IPatientSearchService
    // ─────────────────────────────────────────────────────────────────────────

    public async Task<PatientSearchResponseDto> SearchPatientsAsync(
        PatientSearchQuery query,
        Guid               actingUserId,
        string             ipAddress,
        string             userAgent,
        CancellationToken  ct = default)
    {
        var cacheKey = BuildSearchCacheKey(query);

        var result = await _cache.GetOrSetAsync<PatientSearchResponseDto>(
            cacheKey,
            async () => await ExecuteSearchAsync(query, ct),
            CacheTtl,
            ct)
            ?? await ExecuteSearchAsync(query, ct); // Redis-unavailable fallback (AC-4)

        // Always write audit entry regardless of cache hit (AC-1, NFR-012).
        await _audit.LogAsync(
            AuditAction.DataAccess,
            actingUserId,
            resourceType: "PatientSearch",
            ipAddress:    ipAddress,
            userAgent:    userAgent,
            resourceId:   null,
            cancellationToken: ct);

        return result;
    }

    public async Task<IReadOnlyList<ProviderOptionDto>> GetProviderListAsync(
        CancellationToken ct = default)
    {
        return await _cache.GetOrSetAsync<IReadOnlyList<ProviderOptionDto>>(
            "staff:providers:list",
            async () => await QueryProvidersAsync(ct),
            CacheTtl,
            ct)
            ?? await QueryProvidersAsync(ct); // Redis-unavailable fallback (AC-4)
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<PatientSearchResponseDto> ExecuteSearchAsync(
        PatientSearchQuery query,
        CancellationToken  ct)
    {
        var term       = query.Term.Trim();
        var statusFilter = query.Status?.Trim() ?? "All";
        var page       = Math.Max(1, query.Page);
        var pageSize   = Math.Clamp(query.PageSize, 1, 100);

        // ── Build base query ─────────────────────────────────────────────────
        // IgnoreQueryFilters() is required when surfacing inactive (soft-deleted) patients.
        // For "Active"-only searches the global filter is sufficient but we still bypass
        // it and apply the filter explicitly to unify the code path.
        IQueryable<UPACIP.DataAccess.Entities.Patient> baseQuery =
            _db.Patients
               .IgnoreQueryFilters()
               .AsNoTracking();

        // ── Status filter ────────────────────────────────────────────────────
        if (string.Equals(statusFilter, "Active", StringComparison.OrdinalIgnoreCase))
            baseQuery = baseQuery.Where(p => p.DeletedAt == null);
        else if (string.Equals(statusFilter, "Inactive", StringComparison.OrdinalIgnoreCase))
            baseQuery = baseQuery.Where(p => p.DeletedAt != null);
        // "All" → no additional filter

        // ── Text search ──────────────────────────────────────────────────────
        if (!string.IsNullOrWhiteSpace(term))
        {
            var pattern = $"%{term}%";
            baseQuery = baseQuery.Where(p =>
                EF.Functions.ILike(p.FullName,    pattern) ||
                EF.Functions.ILike(p.PhoneNumber, pattern) ||
                EF.Functions.ILike(p.Email,       pattern));
        }

        // ── Provider filter via latest appointment ───────────────────────────
        if (!string.IsNullOrWhiteSpace(query.Provider))
        {
            baseQuery = baseQuery.Where(p =>
                _db.Appointments.Any(a =>
                    a.PatientId == p.Id &&
                    a.ProviderId.HasValue &&
                    a.ProviderId.Value.ToString() == query.Provider));
        }

        // ── Count ────────────────────────────────────────────────────────────
        var total = await baseQuery.CountAsync(ct);

        // ── Paginate and project ─────────────────────────────────────────────
        var rawRows = await baseQuery
            .OrderBy(p => p.FullName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new
            {
                p.Id,
                p.FullName,
                p.DateOfBirth,
                p.PhoneNumber,
                p.DeletedAt,
                LatestAppointment = _db.Appointments
                    .Where(a => a.PatientId == p.Id)
                    .OrderByDescending(a => a.AppointmentTime)
                    .Select(a => new { a.AppointmentTime, a.ProviderName })
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var patients = rawRows
            .Select(r => new PatientSummaryDto(
                PatientId:   r.Id.ToString(),
                FullName:    r.FullName,
                Mrn:         BuildMrn(r.Id),
                DateOfBirth: r.DateOfBirth.ToString("yyyy-MM-dd"),
                Phone:       r.PhoneNumber,
                Provider:    r.LatestAppointment?.ProviderName ?? string.Empty,
                LastVisitAt: r.LatestAppointment?.AppointmentTime
                               .ToString("yyyy-MM-ddTHH:mm:ssZ"),
                Status: r.DeletedAt == null ? "Active" : "Inactive"))
            .ToList();

        return new PatientSearchResponseDto(patients, total, page, pageSize);
    }

    private async Task<IReadOnlyList<ProviderOptionDto>> QueryProvidersAsync(
        CancellationToken ct)
    {
        return await _db.Appointments
            .AsNoTracking()
            .Where(a => a.ProviderId.HasValue && !string.IsNullOrEmpty(a.ProviderName))
            .GroupBy(a => new { a.ProviderId, a.ProviderName })
            .Select(g => new ProviderOptionDto(
                g.Key.ProviderId!.Value.ToString(),
                g.Key.ProviderName!))
            .OrderBy(p => p.DisplayName)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Derives a deterministic 6-character MRN display token from the patient GUID.
    /// Uses the last 6 hex digits of the UUID N-format string (no separators).
    /// </summary>
    private static string BuildMrn(Guid id)
    {
        var hex = id.ToString("N"); // 32 hex chars, no hyphens
        return $"MRN-{hex[^6..].ToUpper()}";
    }

    /// <summary>
    /// Builds a deterministic, PII-safe cache key by hashing the query parameters
    /// with SHA-256 and taking the first 16 hex characters (64-bit prefix).
    /// </summary>
    private static string BuildSearchCacheKey(PatientSearchQuery query)
    {
        var raw = $"{query.Term}|{query.Provider}|{query.Status}|{query.Page}|{query.PageSize}";
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        return $"patient:search:{hash[..16]}";
    }
}
