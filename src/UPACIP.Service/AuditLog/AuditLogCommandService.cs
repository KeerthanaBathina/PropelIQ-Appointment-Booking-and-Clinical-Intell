using Microsoft.Extensions.Logging;
using UPACIP.Contracts.Models;
using UPACIP.Contracts.Services;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.AuditLogManagement;

/// <summary>
/// CQRS write-side implementation for audit log entry creation (US_096, AC-3, DR-016).
///
/// Uses <see cref="ApplicationDbContext"/> (the transactional write context) to persist
/// immutable audit entries. No update or delete operations are exposed.
///
/// <para>
/// Field mapping from <see cref="AuditLogEntry"/> (Contracts DTO) to
/// <see cref="UPACIP.DataAccess.Entities.AuditLog"/> (DataAccess entity):
/// <list type="table">
///   <item><term>Entry.EntityType</term><description>→ Entity.ResourceType</description></item>
///   <item><term>Entry.EntityId</term><description>→ Entity.ResourceId</description></item>
///   <item><term>Entry.IpAddress</term><description>→ Entity.IpAddress</description></item>
///   <item><term>Entry.OldValues / NewValues / CorrelationId</term><description>Not persisted —
///     the <c>AuditLog</c> entity does not have these columns. Future migration required.</description></item>
/// </list>
/// </para>
///
/// Scoped lifetime — one instance per HTTP request.
/// </summary>
public sealed class AuditLogCommandService : IAuditLogCommandService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AuditLogCommandService> _logger;

    public AuditLogCommandService(
        ApplicationDbContext db,
        ILogger<AuditLogCommandService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<Guid> AppendAsync(AuditLogEntry entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ValidateEntry(entry);

        var entity = MapToEntity(entry);
        _db.AuditLogs.Add(entity);
        await _db.SaveChangesAsync(ct);

        _logger.LogDebug(
            "AUDIT_LOG_APPENDED LogId={LogId} Action={Action} Entity={EntityType}/{EntityId} UserId={UserId}",
            entity.LogId, entity.Action, entity.ResourceType, entity.ResourceId, entity.UserId);

        return entity.LogId;
    }

    /// <inheritdoc/>
    public async Task AppendBatchAsync(
        IReadOnlyList<AuditLogEntry> entries,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
            throw new ArgumentException("Batch must contain at least one entry.", nameof(entries));

        foreach (var entry in entries)
            ValidateEntry(entry);

        var entities = entries.Select(MapToEntity).ToList();
        _db.AuditLogs.AddRange(entities);
        await _db.SaveChangesAsync(ct);

        _logger.LogDebug(
            "AUDIT_LOG_BATCH_APPENDED Count={Count}",
            entities.Count);
    }

    // ── Private helpers ────────────────────────────────────────────────────────────────

    private static void ValidateEntry(AuditLogEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.Action))
            throw new ArgumentException("AuditLogEntry.Action is required.", nameof(entry));

        if (string.IsNullOrWhiteSpace(entry.EntityType))
            throw new ArgumentException("AuditLogEntry.EntityType is required.", nameof(entry));
    }

    private static DataAccess.Entities.AuditLog MapToEntity(AuditLogEntry entry)
    {
        // Parse Action string to enum; default to DataModify for unrecognised values.
        var action = Enum.TryParse<AuditAction>(entry.Action, ignoreCase: true, out var parsed)
            ? parsed
            : AuditAction.DataModify;

        return new DataAccess.Entities.AuditLog
        {
            LogId        = Guid.NewGuid(),
            UserId       = entry.UserId,
            Action       = action,
            ResourceType = entry.EntityType,
            ResourceId   = entry.EntityId,
            IpAddress    = entry.IpAddress ?? string.Empty,
            UserAgent    = string.Empty,              // CorrelationId not in entity schema
            Timestamp    = DateTime.UtcNow,
        };
    }
}
