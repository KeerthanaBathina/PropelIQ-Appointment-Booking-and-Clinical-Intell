namespace UPACIP.Service.Notifications;

/// <summary>
/// Orchestrates appointment-event email delivery: composes templates, invokes the
/// SMTP transport layer, and persists delivery outcomes in <c>NotificationLog</c>.
///
/// Callers supply a <see cref="NotificationEmailRequest"/> describing the appointment
/// context; the service handles template rendering, transport selection, outcome
/// persistence, bounce flagging, and operational alerting internally.
///
/// This interface is intentionally workflow-agnostic so it is reusable across all
/// EP-005 notification triggers (booking confirmation, 24h reminder, 2h reminder,
/// waitlist offer, slot-swap).
/// </summary>
public interface INotificationEmailService
{
    /// <summary>
    /// Composes and sends a notification email for the appointment described by
    /// <paramref name="request"/>, then records the outcome in <c>NotificationLog</c>.
    /// </summary>
    /// <param name="request">
    /// Appointment-context payload containing patient details, appointment metadata,
    /// and the notification type that drives template selection.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="NotificationEmailResult"/> describing whether the email was sent,
    /// bounced, or failed, so callers can react without re-querying the database.
    /// This method never throws; all error conditions are encoded in the result.
    /// </returns>
    Task<NotificationEmailResult> SendAsync(
        NotificationEmailRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// High-level outcome returned to callers of <see cref="INotificationEmailService.SendAsync"/>.
/// </summary>
/// <param name="Succeeded">Whether the email was accepted by an SMTP provider.</param>
/// <param name="IsBounced">
/// <c>true</c> when the recipient address was permanently rejected.
/// Callers may use this to trigger additional UX or staffing workflows.
/// </param>
/// <param name="UsedFallbackProvider">
/// <c>true</c> when the email was delivered via the fallback SMTP provider
/// (e.g. Gmail) after the primary provider exhausted its quota (EC-1).
/// </param>
/// <param name="ProviderName">Name of the provider used for final delivery.</param>
/// <param name="AttemptsMade">Total send attempts made across all providers.</param>
public sealed record NotificationEmailResult(
    bool Succeeded,
    bool IsBounced,
    bool UsedFallbackProvider,
    string ProviderName,
    int AttemptsMade);
