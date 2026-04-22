namespace UPACIP.Service.Notifications;

/// <summary>
/// Orchestrates appointment-event SMS delivery: checks patient opt-out preference,
/// invokes the Twilio transport layer, and persists delivery outcomes in
/// <c>NotificationLog</c>.
///
/// Callers supply a <see cref="NotificationSmsRequest"/> describing the appointment
/// context; the service handles opt-out enforcement, transport invocation, outcome
/// persistence, and operational alerting internally.
///
/// This interface is intentionally workflow-agnostic so it is reusable across all
/// EP-005 SMS triggers (booking confirmation, 24h reminder, 2h reminder,
/// waitlist offer, slot-swap).
///
/// Opt-out contract (AC-2, EC-2):
/// When the patient has opted out of SMS, the service skips transport invocation
/// and records <c>OptedOut</c> as a first-class delivery outcome — it is NOT counted
/// as a failed delivery.
///
/// Gateway-disabled contract (EC-1):
/// When the SMS gateway is disabled or Twilio credits are exhausted, the service
/// emits a <c>LogCritical</c> admin alert and returns <see cref="NotificationSmsResult"/>
/// with <see cref="NotificationSmsResult.IsGatewayDisabled"/> set so the caller can
/// continue in email-only mode.
/// </summary>
public interface INotificationSmsService
{
    /// <summary>
    /// Checks patient opt-out preference, composes and sends an SMS for the appointment
    /// described by <paramref name="request"/>, then records the outcome in
    /// <c>NotificationLog</c>.
    /// </summary>
    /// <param name="request">
    /// Appointment-context payload containing the patient phone number, patient ID
    /// (used for opt-out lookup), appointment metadata, and the notification type
    /// that drives message composition.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="NotificationSmsResult"/> describing the delivery outcome so callers
    /// can react (e.g. fall through to email-only delivery) without re-querying the
    /// database.  This method never throws; all error conditions are encoded in the result.
    /// </returns>
    Task<NotificationSmsResult> SendAsync(
        NotificationSmsRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// High-level outcome returned to callers of <see cref="INotificationSmsService.SendAsync"/>.
/// </summary>
/// <param name="Succeeded">Whether the SMS was accepted by Twilio.</param>
/// <param name="IsOptedOut">
/// <c>true</c> when the patient had opted out of SMS notifications before delivery
/// was attempted.  Transport was NOT invoked; <c>OptedOut</c> was persisted to
/// <c>NotificationLog</c> (AC-2, EC-2).
/// </param>
/// <param name="IsGatewayDisabled">
/// <c>true</c> when the SMS gateway is administratively disabled or Twilio trial
/// credits are exhausted.  Callers SHOULD continue in email-only mode (EC-1).
/// </param>
/// <param name="IsInvalidNumber">
/// <c>true</c> when the recipient phone number was rejected as outside the
/// allowed country code scope (Phase 1: US <c>+1</c> only).
/// </param>
/// <param name="AttemptsMade">Total Twilio API attempts made (0 when opted-out or gateway disabled).</param>
/// <param name="TwilioMessageSid">
/// Twilio Message SID returned on successful delivery (e.g. <c>SM…</c>).
/// <c>null</c> for all non-successful outcomes.
/// </param>
public sealed record NotificationSmsResult(
    bool Succeeded,
    bool IsOptedOut,
    bool IsGatewayDisabled,
    bool IsInvalidNumber,
    int AttemptsMade,
    string? TwilioMessageSid = null);
