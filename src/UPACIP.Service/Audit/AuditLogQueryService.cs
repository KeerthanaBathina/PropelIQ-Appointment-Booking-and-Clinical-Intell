using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using UPACIP.DataAccess;

namespace UPACIP.Service.Audit;

/// <summary>
/// CQRS read-side implementation for audit log queries (US_064 AC-3, Architecture Decision #5).
///
/// Design notes:
///   - <c>AsNoTracking()</c> on all reads — audit entries are never updated via this path.
///   - IQueryable filter composition — each filter clause is applied conditionally so that
///     no unnecessary SQL predicates are emitted for unset parameters (NFR-013).
///   - Keyset pagination: <c>WHERE log_id &gt; @cursor ORDER BY log_id ASC</c> gives O(1) seek
///     performance regardless of how deep the caller pages into the result set (TR-013).
///   - TotalCount uses a separate <c>CountAsync()</c> over the unsliced filtered query;
///     callers should consider caching this if called frequently with the same filters.
///   - PII (IpAddress, UserAgent) is returned in the DTO for authorised admin callers only.
///     It MUST NOT be written to application logs (NFR-017) — note the LogDebug call below
///     does NOT include PII fields.
/// </summary>
public sealed class AuditLogQueryService : IAuditLogQueryService
{
    private readonly ApplicationDbContext         _db;
    private readonly AuditSettings                _settings;
    private readonly ILogger<AuditLogQueryService> _logger;

    public AuditLogQueryService(
        ApplicationDbContext           db,
        IOptions<AuditSettings>        settings,
        ILogger<AuditLogQueryService>  logger)
    {
        _db       = db;
        _settings = settings.Value;
        _logger   = logger;
    }

    /// <inheritdoc/>
    public async Task<AuditLogQueryResponse> QueryAsync(
        AuditLogQueryRequest request,
        CancellationToken    ct = default)
    {
        // Clamp page size to configured maximum to prevent unbounded result sets (NFR-040).
        var effectivePageSize = Math.Clamp(request.PageSize, 1, _settings.QueryMaxPageSize);

        // Build the base filtered query — no tracking needed for read-only projections.
        var baseQuery = _db.AuditLogs
            .AsNoTracking()
            .AsQueryable();

        // ── Optional filters ────────────────────────────────────────────────────────────────

        if (request.UserId.HasValue)
            baseQuery = baseQuery.Where(a => a.UserId == request.UserId.Value);

        if (request.ActionType.HasValue)
            baseQuery = baseQuery.Where(a => a.Action == request.ActionType.Value);

        if (!string.IsNullOrWhiteSpace(request.EntityType))
            baseQuery = baseQuery.Where(a => a.ResourceType == request.EntityType);

        if (request.EntityId.HasValue)
            baseQuery = baseQuery.Where(a => a.ResourceId == request.EntityId.Value);

        if (request.StartDate.HasValue)
            baseQuery = baseQuery.Where(a => a.Timestamp >= request.StartDate.Value);

        if (request.EndDate.HasValue)
            baseQuery = baseQuery.Where(a => a.Timestamp <= request.EndDate.Value);

        // TotalCount is computed over the fully-filtered (pre-cursor) query.
        var totalCount = await baseQuery.LongCountAsync(ct);

        // ── Keyset cursor pagination ─────────────────────────────────────────────────────────
        // Filter to entries after the last seen LogId.  LogId is a UUID v4 so ORDER BY LogId
        // is random-ordered; for stable chronological pagination prefer Timestamp + LogId composite.
        // Here we use LogId alone because it is the primary key and uniquely identifies a row;
        // callers should use the returned NextCursor (which is a LogId) verbatim.
        if (request.Cursor.HasValue)
            baseQuery = baseQuery.Where(a => a.LogId.CompareTo(request.Cursor.Value) > 0);

        // ── Projection with user join ────────────────────────────────────────────────────────
        // Fetch one extra row to detect whether another page exists without a separate COUNT.
        var rawItems = await baseQuery
            .OrderBy(a => a.LogId)
            .Take(effectivePageSize + 1)
            .Select(a => new AuditLogEntryDto(
                a.LogId,
                a.UserId,
                a.User != null ? a.User.Email : null,
                a.User != null ? a.User.FullName : null,
                a.Action,
                a.ResourceType,
                a.ResourceId,
                a.Timestamp,
                a.IpAddress,
                a.UserAgent))
            .ToListAsync(ct);

        var hasMore   = rawItems.Count > effectivePageSize;
        var items     = hasMore ? rawItems[..effectivePageSize] : rawItems;
        var nextCursor = hasMore ? items[^1].LogId : (Guid?)null;

        _logger.LogDebug(
            "AuditLogQueryService: returned {Count}/{Total} entries. HasMore={HasMore} Cursor={Cursor}",
            items.Count, totalCount, hasMore, nextCursor);

        return new AuditLogQueryResponse(items, nextCursor, totalCount, hasMore);
    }
}
