namespace UPACIP.Service.Auth;

/// <summary>
/// Encapsulates audit logging for account lockout events (US_065 AC-3, HIPAA §164.312(a)).
///
/// Separates lockout audit concerns from <c>AuthController</c> so the controller does not
/// accumulate responsibility for structured lockout metadata logging.
/// </summary>
public interface IAccountLockoutAuditHandler
{
    /// <summary>
    /// Logs a single failed login attempt to the audit trail (AuditAction.FailedLogin).
    /// Called after each credential validation failure, BEFORE any lockout is applied.
    /// </summary>
    Task LogFailedAttemptAsync(
        Guid userId,
        string ipAddress,
        string userAgent,
        int failedAttemptCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Logs an account lockout event to the audit trail (AuditAction.AccountLocked).
    /// Called when ASP.NET Core Identity triggers a lockout after the failed-attempt
    /// threshold is reached (default: 5 consecutive failures → 30-minute lockout per NFR-016).
    /// Admin accounts receive identical treatment — no lockout bypass exists (edge case).
    /// </summary>
    Task LogLockoutAsync(
        Guid userId,
        string ipAddress,
        string userAgent,
        DateTime? lockoutEndUtc,
        CancellationToken cancellationToken = default);
}
