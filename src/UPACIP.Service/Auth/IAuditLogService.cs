using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Auth;

/// <summary>
/// Insert-only audit log service (US_016 AC-5).
/// All authentication events are recorded as immutable entries — no update or delete operations
/// are exposed to enforce append-only semantics (DR-016).
/// </summary>
public interface IAuditLogService
{
    /// <summary>
    /// Inserts an immutable audit log entry for an authentication event.
    /// <paramref name="userId"/> may be <c>null</c> for unauthenticated events where the
    /// user cannot be identified (handled gracefully — log is skipped rather than failing).
    /// </summary>
    Task LogAsync(
        AuditAction action,
        Guid?       userId,
        string      resourceType,
        string      ipAddress,
        string      userAgent,
        Guid?       resourceId        = null,
        CancellationToken cancellationToken = default);
}
