using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.Contracts.Models;
using UPACIP.Contracts.Services;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.AuditLogManagement;

/// <summary>
/// CQRS read-side implementation for audit log compliance queries (US_096, AC-3, TR-013).
///
/// Uses <see cref="AuditLogReadDbContext"/> — a dedicated read-only DbContext configured
/// with global <c>NoTracking</c> and no navigation-property loading. This isolates read
/// load from the transactional write path (<c>ApplicationDbContext</c>).
///
/// Query design:
/// <list type="bullet">
///   <item>All filters are applied conditionally — unset parameters produce no SQL predicate.</item>
///   <item>Pagination uses offset/limit (<c>Skip</c>/<c>Take</c>) via <see cref="PagedResult{T}"/>.</item>
///   <item>Entity-specific queries use the <c>IX_AuditLogs_Entity</c> composite index.</item>
///   <item>Date-range count uses the <c>ix_audit_logs_timestamp</c> index.</item>
///   <item>PII fields (IpAddress) are included in projections for authorised admin callers only.</item>
/// </list>
///
/// Scoped lifetime — one instance per HTTP request.
/// </summary>
public sealed class AuditLogQueryService : IAuditLogQueryService
{
    private readonly AuditLogReadDbContext _readContext;
    private readonly ILogger<AuditLogQueryService> _logger;

    public AuditLogQueryService(
        AuditLogReadDbContext readContext,
        ILogger<AuditLogQueryService> logger)
    {
        _readContext = readContext;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<PagedResult<AuditLogReadModel>> QueryAsync(
        AuditLogQueryFilter filter,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var page     = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 200);

        var query = BuildFilteredQuery(filter);

        var totalCount = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(a => a.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogReadModel
            {
                Id           = a.LogId,
                TimestampUtc = a.Timestamp,
                UserId       = a.UserId,
                UserName     = null,                 // No user join in read-only context
                Action       = a.Action.ToString(),
                EntityType   = a.ResourceType,
                EntityId     = a.ResourceId,
                IpAddress    = a.IpAddress,
                CorrelationId = null,                // Not in current entity schema
            })
            .ToListAsync(ct);

        _logger.LogDebug(
            "AUDIT_LOG_QUERY Page={Page} PageSize={PageSize} Total={Total}",
            page, pageSize, totalCount);

        return PagedResult<AuditLogReadModel>.Create(items, page, pageSize, totalCount);
    }

    /// <inheritdoc/>
    public async Task<AuditLogReadModel?> GetByIdAsync(Guid auditLogId, CancellationToken ct = default)
    {
        var entity = await _readContext.AuditLogs
            .Where(a => a.LogId == auditLogId)
            .Select(a => new AuditLogReadModel
            {
                Id           = a.LogId,
                TimestampUtc = a.Timestamp,
                UserId       = a.UserId,
                UserName     = null,
                Action       = a.Action.ToString(),
                EntityType   = a.ResourceType,
                EntityId     = a.ResourceId,
                IpAddress    = a.IpAddress,
                CorrelationId = null,
            })
            .FirstOrDefaultAsync(ct);

        return entity;
    }

    /// <inheritdoc/>
    public async Task<List<AuditLogReadModel>> GetByEntityAsync(
        string entityType,
        Guid entityId,
        CancellationToken ct = default)
    {
        return await _readContext.AuditLogs
            .Where(a => a.ResourceType == entityType && a.ResourceId == entityId)
            .OrderByDescending(a => a.Timestamp)
            .Select(a => new AuditLogReadModel
            {
                Id           = a.LogId,
                TimestampUtc = a.Timestamp,
                UserId       = a.UserId,
                UserName     = null,
                Action       = a.Action.ToString(),
                EntityType   = a.ResourceType,
                EntityId     = a.ResourceId,
                IpAddress    = a.IpAddress,
                CorrelationId = null,
            })
            .ToListAsync(ct);
    }

    /// <inheritdoc/>
    public async Task<int> CountByDateRangeAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct = default)
    {
        return await _readContext.AuditLogs
            .CountAsync(a => a.Timestamp >= fromUtc && a.Timestamp <= toUtc, ct);
    }

    // ── Private helpers ────────────────────────────────────────────────────────────────

    private IQueryable<DataAccess.Entities.AuditLog> BuildFilteredQuery(AuditLogQueryFilter filter)
    {
        var query = _readContext.AuditLogs.AsQueryable();

        if (filter.FromUtc.HasValue)
            query = query.Where(a => a.Timestamp >= filter.FromUtc.Value);

        if (filter.ToUtc.HasValue)
            query = query.Where(a => a.Timestamp <= filter.ToUtc.Value);

        if (filter.UserId.HasValue)
            query = query.Where(a => a.UserId == filter.UserId.Value);

        if (!string.IsNullOrWhiteSpace(filter.EntityType))
            query = query.Where(a => a.ResourceType == filter.EntityType);

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            // Parse the string filter to AuditAction enum for type-safe comparison.
            if (Enum.TryParse<AuditAction>(filter.Action, ignoreCase: true, out var parsedAction))
                query = query.Where(a => a.Action == parsedAction);
        }

        if (filter.EntityId.HasValue)
            query = query.Where(a => a.ResourceId == filter.EntityId.Value);

        // Note: CorrelationId filter not supported — field not in AuditLog entity schema.

        return query;
    }
}
