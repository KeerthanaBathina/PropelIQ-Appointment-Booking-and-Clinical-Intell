using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Auth;

/// <summary>
/// Append-only audit log service backed by EF Core (US_016 AC-5).
///
/// Design notes:
///   - Only <c>INSERT</c> operations are exposed — no update or delete methods exist.
///   - Failures are logged via Serilog but never propagate to the caller so a log write
///     failure never breaks the authentication pipeline (fail-open for availability).
///   - IP extraction supports X-Forwarded-For via the caller (AuthController extracts and passes it).
/// </summary>
public sealed class AuditLogService : IAuditLogService
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(ApplicationDbContext db, ILogger<AuditLogService> logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task LogAsync(
        AuditAction action,
        Guid?       userId,
        string      resourceType,
        string      ipAddress,
        string      userAgent,
        Guid?       resourceId        = null,
        CancellationToken cancellationToken = default)
    {
        // Skip insert-only audit entries that have no identifiable user to avoid ambiguity.
        // Unauthenticated events (e.g. failed login with unknown email) are not persisted
        // to avoid noise in the per-user audit trail (OWASP A07 anti-enumeration).
        if (userId is null || userId == Guid.Empty)
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
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
