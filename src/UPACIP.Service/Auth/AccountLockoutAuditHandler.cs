using Microsoft.Extensions.Logging;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Auth;

/// <summary>
/// Writes HIPAA-required audit log entries for account lockout events (US_065 AC-3, NFR-016).
///
/// Design:
///   - Delegates persistence to <see cref="IAuditLogService"/> (same as all other audit writes).
///   - Structured log metadata (failed attempt count, lockout end UTC) is written via
///     <see cref="ILogger"/> as structured properties — NOT embedded in the AuditLog entity
///     to avoid schema changes. This keeps PII in the logging pipeline where it is
///     retention-managed, while the AuditLog table captures the mandatory HIPAA fields.
///   - Never throws — failures are caught and logged so the auth pipeline is unaffected
///     (fail-open per NFR-012).
/// </summary>
public sealed class AccountLockoutAuditHandler : IAccountLockoutAuditHandler
{
    private readonly IAuditLogService                   _auditLogService;
    private readonly ILogger<AccountLockoutAuditHandler> _logger;

    public AccountLockoutAuditHandler(
        IAuditLogService                    auditLogService,
        ILogger<AccountLockoutAuditHandler> logger)
    {
        _auditLogService = auditLogService;
        _logger          = logger;
    }

    /// <inheritdoc/>
    public async Task LogFailedAttemptAsync(
        Guid userId,
        string ipAddress,
        string userAgent,
        int failedAttemptCount,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Structured log for monitoring dashboards — failedAttemptCount must NOT
            // appear in AuditLog (no metadata column) but IS captured in the app log (NFR-017).
            _logger.LogWarning(
                "Failed login attempt #{FailedAttemptCount} for user {UserId} from {IpAddress}.",
                failedAttemptCount, userId, ipAddress);

            await _auditLogService.LogAsync(
                AuditAction.FailedLogin,
                userId,
                resourceType: "User",
                ipAddress:    ipAddress,
                userAgent:    userAgent,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AccountLockoutAuditHandler: failed to write FailedLogin audit for user {UserId}.", userId);
        }
    }

    /// <inheritdoc/>
    public async Task LogLockoutAsync(
        Guid userId,
        string ipAddress,
        string userAgent,
        DateTime? lockoutEndUtc,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogWarning(
                "Account locked for user {UserId} from {IpAddress}. LockoutEnd={LockoutEndUtc:O}.",
                userId, ipAddress, lockoutEndUtc);

            await _auditLogService.LogAsync(
                AuditAction.AccountLocked,
                userId,
                resourceType: "User",
                ipAddress:    ipAddress,
                userAgent:    userAgent,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "AccountLockoutAuditHandler: failed to write AccountLocked audit for user {UserId}.", userId);
        }
    }
}
