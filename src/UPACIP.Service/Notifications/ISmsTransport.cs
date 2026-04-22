namespace UPACIP.Service.Notifications;

/// <summary>
/// Low-level Twilio SMS transport abstraction.  Higher-level notification workflows
/// (appointment confirmations, reminders, waitlist offers) depend on this interface
/// rather than on Twilio SDK types so the provider can be swapped or mocked without
/// changing service logic.
///
/// Responsibilities of implementations:
/// <list type="bullet">
///   <item>Validate the recipient number against the Phase 1 country-code scope (EC-2).</item>
///   <item>Return <see cref="SmsDeliveryOutcome.GatewayDisabled"/> immediately when SMS is disabled (EC-1).</item>
///   <item>Retry transient Twilio API failures with exponential backoff (1s, 2s, 4s) — AC-3.</item>
///   <item>Enforce the 30-second delivery timeout — AC-1.</item>
///   <item>Emit structured diagnostics without leaking credentials or patient PII.</item>
/// </list>
///
/// This interface never throws.  All outcomes are encoded in <see cref="SmsDeliveryAttemptResult"/>.
/// </summary>
public interface ISmsTransport
{
    /// <summary>
    /// Sends <paramref name="message"/> via Twilio SMS, applying the configured
    /// US-number validation, retry, and timeout policy.
    /// </summary>
    /// <param name="message">
    /// SMS message to deliver (recipient phone number and text body).
    /// Must not be <c>null</c>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token.  When cancelled, any in-flight Twilio API call is
    /// abandoned and a <see cref="SmsDeliveryOutcome.Failed"/> result is returned
    /// rather than propagating <see cref="OperationCanceledException"/>.
    /// </param>
    /// <returns>
    /// A <see cref="SmsDeliveryAttemptResult"/> describing whether the message was
    /// accepted (<see cref="SmsDeliveryOutcome.Sent"/>), rejected due to scope
    /// (<see cref="SmsDeliveryOutcome.InvalidNumber"/>), skipped because the gateway
    /// is disabled (<see cref="SmsDeliveryOutcome.GatewayDisabled"/>), or failed
    /// after retries (<see cref="SmsDeliveryOutcome.Failed"/>).
    /// </returns>
    Task<SmsDeliveryAttemptResult> SendAsync(
        SmsTransportMessage message,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Fully-composed SMS message passed to <see cref="ISmsTransport.SendAsync"/>.
/// </summary>
/// <param name="ToPhoneNumber">
/// Recipient phone number in E.164 format (e.g. <c>+12025551234</c>).
/// Phase 1 accepts US <c>+1</c> numbers only.
/// </param>
/// <param name="Body">
/// SMS message text.  Twilio enforces a 1600-character limit per segment.
/// </param>
public sealed record SmsTransportMessage(
    string ToPhoneNumber,
    string Body);
