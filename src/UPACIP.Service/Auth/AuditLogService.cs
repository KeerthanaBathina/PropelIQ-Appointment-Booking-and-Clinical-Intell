using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Audit;

namespace UPACIP.Service.Auth;

/// <summary>
/// Append-only audit log service backed by EF Core (US_016 AC-5).
///
/// Design notes:
///   - Only <c>INSERT</c> operations are exposed — no update or delete methods exist.
///   - Failures are logged via Serilog but never propagate to the caller so a log write
///     failure never breaks the authentication pipeline (fail-open for availability).
///   - On DB write failure, entries are delegated to <see cref="IAuditQueueService"/> for
///     Redis-backed failover queuing with local file last-resort (US_064 edge case).
///   - IP extraction supports X-Forwarded-For via the caller (AuthController extracts and passes it).
/// </summary>
public sealed class AuditLogService : IAuditLogService
{
    private readonly ApplicationDbContext    _db;
    private readonly IAuditQueueService      _auditQueue;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(
        ApplicationDbContext     db,
        IAuditQueueService       auditQueue,
        ILogger<AuditLogService> logger)
    {
        _db         = db;
        _auditQueue = auditQueue;
        _logger     = logger;
    }

    /// <inheritdoc/>
    public async Task LogAsync(
        AuditAction action,
        Guid?       userId,
        string      resourceType,
        string      ipAddress,
        string      userAgent,
        Guid?       resourceId        = null,
        CancellationToken cancellationToken = default,
        bool        systemEvent       = false)
    {
        // Skip unauthenticated events where the user cannot be identified (OWASP A07
        // anti-enumeration: failed login with unknown email, etc.).
        // System-generated events (e.g. auto no-show detection) pass systemEvent=true
        // and are always persisted even with a null userId (US_055 AC-4).
        if (!systemEvent && (userId is null || userId == Guid.Empty))
        {
            _logger.LogDebug("AuditLogService: skipping log for action {Action} — no user identity.", action);
            return;
        }

        try
        {
            var entry = new AuditLog
            {
                LogId        = Guid.NewGuid(),
                UserId       = userId,   // Guid? — null handled by EF (FK ON DELETE SET NULL)
                Action       = action,
                ResourceType = resourceType,
                ResourceId   = resourceId,
                Timestamp    = DateTime.UtcNow,
                IpAddress    = Truncate(ipAddress,  45),
                UserAgent    = Truncate(userAgent,  500),
            };

            _db.AuditLogs.Add(entry);
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Fail-open: log write failures must never abort the auth request.
            _logger.LogError(ex, "Failed to write audit log entry for action {Action}, user {UserId}.", action, userId);

            // Delegate to Redis failover queue so the entry is not lost (US_064 edge case).
            // AuditQueueService is itself fail-open — if Redis is also down it falls back to
            // a local file and logs Critical but never throws.
            await _auditQueue.EnqueueAsync(new AuditLogQueueEntry
            {
                LogId        = Guid.NewGuid(),
                UserId       = userId,
                Action       = action.ToString(),
                ResourceType = resourceType,
                ResourceId   = resourceId,
                Timestamp    = DateTime.UtcNow,
                IpAddress    = Truncate(ipAddress,  45),
                UserAgent    = Truncate(userAgent,  500),
                EnqueuedAt   = DateTime.UtcNow,
            }, cancellationToken);
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
