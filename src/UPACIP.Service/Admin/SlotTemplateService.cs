using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Auth;
using UPACIP.Service.Caching;

namespace UPACIP.Service.Admin;

/// <summary>
/// Manages appointment slot templates for the Admin Configuration UI (US_059 AC-1, AC-2).
///
/// <para>
/// Each template is stored as a <see cref="SlotTemplate"/> header row with one or more
/// <see cref="SlotTemplateBlock"/> child rows.  Updates replace all blocks atomically via
/// EF Core tracking (cascade delete + re-insert).
/// </para>
///
/// Redis cache keys (5-min TTL, NFR-030):
///   <c>config:slots:{providerId}:{dayOfWeek}</c>  — single template GET
///   <c>config:slots:{providerId}</c>               — all-days GET for a provider
///
/// Every write appends an AuditLog entry (NFR-012, NFR-035).
/// </summary>
public sealed class SlotTemplateService : ISlotTemplateService
{
    // ── Cache key helpers ─────────────────────────────────────────────────────
    private static string CacheKeyDay(Guid providerId, int day) =>
        $"config:slots:{providerId}:{day}";
    private static string CacheKeyProvider(Guid providerId) =>
        $"config:slots:{providerId}";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly ApplicationDbContext       _db;
    private readonly ICacheService              _cache;
    private readonly IAuditLogService           _audit;
    private readonly ILogger<SlotTemplateService> _logger;

    public SlotTemplateService(
        ApplicationDbContext         db,
        ICacheService                cache,
        IAuditLogService             audit,
        ILogger<SlotTemplateService> logger)
    {
        _db     = db;
        _cache  = cache;
        _audit  = audit;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Reads
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<SlotTemplateResponse?> GetByProviderAndDayAsync(
        Guid providerId, int dayOfWeek, CancellationToken ct = default)
    {
        var cacheKey = CacheKeyDay(providerId, dayOfWeek);
        var cached   = await _cache.GetAsync<SlotTemplateResponse>(cacheKey, ct);
        if (cached is not null) return cached;

        var entity = await _db.SlotTemplates
            .AsNoTracking()
            .Include(t => t.Blocks)
            .FirstOrDefaultAsync(
                t => t.ProviderId == providerId && t.DayOfWeek == dayOfWeek, ct);

        if (entity is null) return null;

        var response = MapToResponse(entity);
        await _cache.SetAsync(cacheKey, response, CacheTtl, ct);
        return response;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SlotTemplateResponse>> GetByProviderAsync(
        Guid providerId, CancellationToken ct = default)
    {
        var cacheKey = CacheKeyProvider(providerId);
        var cached   = await _cache.GetAsync<List<SlotTemplateResponse>>(cacheKey, ct);
        if (cached is not null) return cached;

        var entities = await _db.SlotTemplates
            .AsNoTracking()
            .Include(t => t.Blocks)
            .Where(t => t.ProviderId == providerId)
            .OrderBy(t => t.DayOfWeek)
            .ToListAsync(ct);

        var responses = entities.Select(MapToResponse).ToList();
        await _cache.SetAsync(cacheKey, responses, CacheTtl, ct);
        return responses;
    }

    /// <inheritdoc/>
    public async Task<AffectedAppointmentsResponse> GetAffectedAppointmentsAsync(
        Guid providerId, int dayOfWeek, CancellationToken ct = default)
    {
        // Map dayOfWeek int to DayOfWeek enum value used in AppointmentTime comparison.
        // Appointments are stored with DateTime UTC; we compare DayOfWeek on the UTC date.
        var affected = await _db.Appointments
            .AsNoTracking()
            .Where(a =>
                a.ProviderId    == providerId
             && a.Status        != AppointmentStatus.Cancelled
             && (int)a.AppointmentTime.DayOfWeek == dayOfWeek)
            .OrderBy(a => a.AppointmentTime)
            .Select(a => new AffectedAppointmentDto(
                a.Id,
                a.BookingReference,
                a.AppointmentTime,
                a.ProviderName,
                a.AppointmentType))
            .ToListAsync(ct);

        return new AffectedAppointmentsResponse(affected.Count, affected);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Upsert
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<SlotTemplateUpsertResult> UpsertAsync(
        Guid                      providerId,
        int                       dayOfWeek,
        UpsertSlotTemplateRequest request,
        Guid                      adminUserId,
        CancellationToken         ct = default)
    {
        // Validate provider exists (OWASP A01 — no implicit Guid creation for nonexistent users).
        var providerExists = await _db.Users.AnyAsync(u => u.Id == providerId, ct);
        if (!providerExists)
        {
            _logger.LogWarning(
                "SlotTemplate upsert rejected — provider {ProviderId} not found.", providerId);
            return SlotTemplateUpsertResult.ProviderNotFound();
        }

        // Load existing template (tracking) so EF Core can diff block changes.
        var existing = await _db.SlotTemplates
            .Include(t => t.Blocks)
            .FirstOrDefaultAsync(
                t => t.ProviderId == providerId && t.DayOfWeek == dayOfWeek, ct);

        try
        {
            SlotTemplate template;

            if (existing is null)
            {
                // ── Create ─────────────────────────────────────────────────
                template = new SlotTemplate
                {
                    SlotTemplateId = Guid.NewGuid(),
                    ProviderId     = providerId,
                    DayOfWeek      = dayOfWeek,
                    Version        = 0,
                    CreatedAt      = DateTime.UtcNow,
                    UpdatedAt      = DateTime.UtcNow,
                };
                AddBlocks(template, request.Blocks);
                _db.SlotTemplates.Add(template);
            }
            else
            {
                // ── Update — verify client version before proceeding ────────
                if (request.Version is not null && request.Version != existing.Version)
                {
                    _logger.LogWarning(
                        "SlotTemplate concurrency conflict: client Version={Client}, DB Version={Db}, " +
                        "template={TemplateId}.",
                        request.Version, existing.Version, existing.SlotTemplateId);
                    return SlotTemplateUpsertResult.Conflict();
                }

                // Remove old blocks (cascade-delete handled by EF tracking).
                _db.SlotTemplateBlocks.RemoveRange(existing.Blocks);

                // Replace with new blocks.
                existing.Blocks.Clear();
                AddBlocks(existing, request.Blocks);
                existing.Version++;
                existing.UpdatedAt = DateTime.UtcNow;
                template = existing;
            }

            await _db.SaveChangesAsync(ct);

            // ── Audit log ─────────────────────────────────────────────────
            await _audit.LogAsync(
                AuditAction.DataModify,
                adminUserId,
                resourceType:      "SlotTemplate",
                ipAddress:         string.Empty,
                userAgent:         string.Empty,
                resourceId:        template.SlotTemplateId,
                cancellationToken: ct);

            // ── Cache invalidation ─────────────────────────────────────────
            await Task.WhenAll(
                _cache.RemoveAsync(CacheKeyDay(providerId, dayOfWeek), ct),
                _cache.RemoveAsync(CacheKeyProvider(providerId), ct));

            // Reload with blocks for the response (tracks are already saved).
            var saved = await _db.SlotTemplates
                .AsNoTracking()
                .Include(t => t.Blocks)
                .FirstAsync(t => t.SlotTemplateId == template.SlotTemplateId, ct);

            return SlotTemplateUpsertResult.Success(MapToResponse(saved));
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex,
                "SlotTemplate DbUpdateConcurrencyException for provider={ProviderId} day={Day}.",
                providerId, dayOfWeek);
            return SlotTemplateUpsertResult.Conflict();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static void AddBlocks(SlotTemplate template, IReadOnlyList<SlotTemplateBlockDto> blocks)
    {
        foreach (var b in blocks)
        {
            template.Blocks.Add(new SlotTemplateBlock
            {
                BlockId         = Guid.NewGuid(),
                SlotTemplateId  = template.SlotTemplateId,
                StartTime       = b.StartTime,
                EndTime         = b.EndTime,
                AppointmentType = b.AppointmentType,
                IsAvailable     = b.IsAvailable,
                CreatedAt       = DateTime.UtcNow,
            });
        }
    }

    private static SlotTemplateResponse MapToResponse(SlotTemplate t) =>
        new(
            t.SlotTemplateId,
            t.ProviderId,
            t.DayOfWeek,
            t.Version,
            t.CreatedAt,
            t.UpdatedAt,
            t.Blocks
                .OrderBy(b => b.StartTime)
                .Select(b => new SlotTemplateBlockDto(
                    b.BlockId,
                    b.StartTime,
                    b.EndTime,
                    b.AppointmentType,
                    b.IsAvailable))
                .ToList());
}
