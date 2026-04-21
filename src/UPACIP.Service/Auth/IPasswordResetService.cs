namespace UPACIP.Service.Auth;

/// <summary>
/// Defines the password reset flow business operations (US_015).
///
/// OWASP Forgot Password Cheat Sheet compliance:
///   - Anti-enumeration: callers always receive the same public result regardless of
///     whether the email is registered.
///   - Only the latest token per user is valid; prior tokens are explicitly invalidated.
///   - Tokens expire after 1 hour (FR-005).
///   - Successful reset invalidates all active sessions and JWT JTIs for the user.
/// </summary>
public interface IPasswordResetService
{
    /// <summary>
    /// Initiates the password reset flow for <paramref name="email"/>.
    /// If the email is registered a token is generated, stored (hashed), and sent via email.
    /// If the email is not registered the method returns the same success result to
    /// prevent email enumeration (OWASP A07).
    /// Rate-limit enforcement is handled at the controller/middleware layer.
    /// </summary>
    /// <param name="email">The email address submitted by the user.</param>
    /// <param name="requestIpAddress">Originating client IP for audit logging.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Always returns a result with <c>Success = true</c> and the generic anti-enumeration message.
    /// </returns>
    Task<PasswordResetRequestResult> RequestResetAsync(
        string email,
        string requestIpAddress,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the reset token and sets the new password.
    /// After success all active Redis sessions and JWT JTIs for the user are invalidated
    /// so existing sessions cannot continue with the old credentials (AC-4).
    /// </summary>
    /// <param name="email">The email associated with the reset link.</param>
    /// <param name="token">The raw (URL-decoded) reset token from the link.</param>
    /// <param name="newPassword">The new password chosen by the user.</param>
    /// <param name="requestIpAddress">Originating client IP for audit logging.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PasswordResetResult> ResetPasswordAsync(
        string email,
        string token,
        string newPassword,
        string requestIpAddress,
        CancellationToken cancellationToken = default);
}

// ── Result value objects ──────────────────────────────────────────────────────

public sealed record PasswordResetRequestResult(bool Success, string Message);

public enum ResetPasswordOutcome
{
    Success,
    InvalidUser,
    ExpiredToken,
    InvalidToken,
    PasswordComplexityFailed,
}

public sealed record PasswordResetResult(
    ResetPasswordOutcome Outcome,
    string Message,
    IDictionary<string, string[]>? ValidationErrors = null);
