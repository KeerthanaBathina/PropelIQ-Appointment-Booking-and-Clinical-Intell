namespace UPACIP.Service.Notifications;

/// <summary>
/// Low-level SMTP transport abstraction.  Higher-level notification workflows
/// (appointment confirmations, reminders, waitlist offers) depend on this interface
/// rather than on concrete provider clients so provider changes are isolated to a
/// single implementation class.
///
/// Responsibilities of implementations:
/// <list type="bullet">
///   <item>Choose the correct SMTP provider (primary → fallback on quota/availability failure).</item>
///   <item>Retry transient failures with exponential backoff (1s, 2s, 4s) — AC-3.</item>
///   <item>Distinguish bounced addresses from retryable transport errors — EC-2.</item>
///   <item>Emit structured diagnostics without leaking credentials or patient PII.</item>
///   <item>Enforce the 30-second delivery timeout — AC-1.</item>
/// </list>
///
/// This interface intentionally carries no message-composition responsibilities.
/// HTML/plain-text bodies are assembled by the notification orchestration layer
/// and passed in via <paramref name="message"/>.
/// </summary>
public interface IEmailTransport
{
    /// <summary>
    /// Sends <paramref name="message"/> over SMTP, applying the configured retry,
    /// failover, and timeout policy.
    /// </summary>
    /// <param name="message">
    /// Fully-composed email message (recipient, subject, HTML body, plain-text body).
    /// Must not be <c>null</c>.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token.  When cancelled, any in-flight SMTP connection is
    /// abandoned and a <see cref="EmailDeliveryOutcome.Failed"/> result is returned
    /// rather than propagating <see cref="OperationCanceledException"/>, so
    /// callers receive a consistent result contract.
    /// </param>
    /// <returns>
    /// A <see cref="EmailDeliveryAttemptResult"/> describing whether the message
    /// was accepted (<see cref="EmailDeliveryOutcome.Sent"/>), permanently rejected
    /// (<see cref="EmailDeliveryOutcome.Bounced"/>), or failed after retries
    /// (<see cref="EmailDeliveryOutcome.Failed"/>).
    /// This method never throws; all error conditions are encoded in the result.
    /// </returns>
    Task<EmailDeliveryAttemptResult> SendAsync(
        EmailTransportMessage message,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Fully-composed email message passed to <see cref="IEmailTransport.SendAsync"/>.
/// All fields are required; validation is performed by the caller before invoking
/// the transport.
/// </summary>
/// <param name="ToAddress">Recipient email address.</param>
/// <param name="ToName">Recipient display name for the <c>To:</c> header.</param>
/// <param name="Subject">Email subject line.</param>
/// <param name="HtmlBody">HTML content of the message body.</param>
/// <param name="PlainTextBody">Plain-text fallback for clients that cannot render HTML.</param>
public sealed record EmailTransportMessage(
    string ToAddress,
    string ToName,
    string Subject,
    string HtmlBody,
    string PlainTextBody);
