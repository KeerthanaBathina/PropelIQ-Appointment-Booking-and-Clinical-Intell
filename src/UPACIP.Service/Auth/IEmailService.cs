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

    /// <summary>
    /// Sends a waitlist offer notification email containing the claim link (US_020 AC-2).
    /// The link includes a short-lived token; clicking it acquires a 60-second slot hold.
    /// </summary>
    /// <param name="toEmail">Recipient email address.</param>
    /// <param name="toName">Recipient display name.</param>
    /// <param name="claimLink">Full booking claim URL (e.g. /book?claim=TOKEN).</param>
    /// <param name="appointmentDetails">Human-readable summary: date, time, provider.</param>
    /// <param name="isWithin24Hours">True when the slot is within 24 h — extra urgency copy is added.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task SendWaitlistOfferEmailAsync(
        string toEmail,
        string toName,
        string claimLink,
        string appointmentDetails,
        bool isWithin24Hours,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies the patient that their appointment was automatically moved to a preferred slot (US_021 AC-2).
    /// </summary>
    Task SendSwapCompletedEmailAsync(
        string toEmail,
        string toName,
        string oldAppointmentTime,
        string newAppointmentTime,
        string providerName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a manual-confirmation offer for a preferred slot opening within 24 hours (US_021 AC-5).
    /// </summary>
    Task SendManualSwapConfirmationEmailAsync(
        string toEmail,
        string toName,
        string preferredSlotTime,
        string providerName,
        CancellationToken cancellationToken = default);
}
