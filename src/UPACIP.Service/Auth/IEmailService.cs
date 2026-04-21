namespace UPACIP.Service.Auth;

/// <summary>
/// Abstraction for outbound email dispatch.
/// Decouples business logic from the SMTP transport so implementations
/// can be swapped (SendGrid, Gmail SMTP, stub for testing) without
/// changing service code.
/// </summary>
public interface IEmailService
{
    /// <summary>
    /// Sends an email verification message containing a tokenised link to
    /// <paramref name="toEmail"/>. The link expires after 1 hour (AC-3).
    /// </summary>
    /// <param name="toEmail">Recipient email address.</param>
    /// <param name="toName">Recipient display name for personalisation.</param>
    /// <param name="verificationLink">
    /// Full URL the recipient must visit to confirm their account.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendVerificationEmailAsync(
        string toEmail,
        string toName,
        string verificationLink,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a password reset email with a single-use reset link that expires in 1 hour (FR-005).
    /// </summary>
    /// <param name="toEmail">Recipient email address.</param>
    /// <param name="toName">Recipient display name for personalisation.</param>
    /// <param name="resetLink">
    /// Full URL the recipient must visit to set a new password.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendPasswordResetEmailAsync(
        string toEmail,
        string toName,
        string resetLink,
        CancellationToken cancellationToken = default);
}
