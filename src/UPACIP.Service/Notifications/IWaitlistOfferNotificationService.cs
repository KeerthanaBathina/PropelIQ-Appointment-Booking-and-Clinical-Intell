namespace UPACIP.Service.Notifications;

/// <summary>
/// Orchestrates email and SMS delivery for a single waitlist offer candidate (US_036 AC-1).
///
/// Responsibilities:
/// <list type="bullet">
///   <item>Send a waitlist offer email via <see cref="UPACIP.Service.Auth.IEmailService"/> using the
///     pre-existing <c>SendWaitlistOfferEmailAsync</c> method that does not require an
///     <c>AppointmentId</c> FK (no appointment exists at offer time).</item>
///   <item>Consult patient SMS opt-out preference before invoking <see cref="ISmsTransport"/>
///     directly, mirroring the pattern in <see cref="NotificationSmsService"/> (AC-3, EC-2).</item>
///   <item>Detect invalid-contact outcomes — email bounce + invalid/absent phone — so the
///     caller can skip this candidate and advance to the next waitlisted patient (EC-1).</item>
///   <item>Never throw; all outcomes are encoded in <see cref="WaitlistOfferNotificationResult"/>.</item>
/// </list>
/// </summary>
public interface IWaitlistOfferNotificationService
{
    /// <summary>
    /// Dispatches a waitlist offer notification via email and SMS for the candidate
    /// described by <paramref name="request"/>.
    /// </summary>
    /// <param name="request">Slot-availability and candidate context payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="WaitlistOfferNotificationResult"/> describing per-channel outcomes.
    /// Never throws.
    /// </returns>
    Task<WaitlistOfferNotificationResult> SendOfferAsync(
        WaitlistOfferNotificationRequest request,
        CancellationToken cancellationToken = default);
}
