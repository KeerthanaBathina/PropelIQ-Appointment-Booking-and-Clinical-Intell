namespace UPACIP.Service.Notifications;

/// <summary>
/// Coordinates immediate booking-confirmation notification delivery after a
/// successful appointment commit.
///
/// Responsibilities (US_034):
/// <list type="bullet">
///   <item>Send a confirmation email with a prefilled cancellation link (AC-1, AC-3).</item>
///   <item>
///     Attempt PDF generation and attach the document to the email; fall back to
///     email-without-PDF and log a retry-needed flag when generation fails (EC-1).
///   </item>
///   <item>
///     Re-read latest appointment status before composing content so a near-simultaneous
///     cancellation is reflected in the confirmation message rather than stale
///     "scheduled" language (EC-2).
///   </item>
///   <item>Send a confirmation SMS when the patient has not opted out (AC-1, AC-4).</item>
///   <item>Skip SMS and persist <c>OptedOut</c> when the patient has opted out (AC-4).</item>
///   <item>
///     Continue in email-only mode and emit an operational alert when the SMS gateway
///     is disabled or Twilio credits are exhausted (EC-1).
///   </item>
/// </list>
///
/// This interface is intentionally workflow-agnostic so reminder and waitlist
/// notification triggers can reuse the same cancellation-link and state-check patterns.
/// This method never throws; all outcomes are encoded in
/// <see cref="BookingConfirmationNotificationResult"/>.
/// </summary>
public interface IBookingConfirmationNotificationService
{
    /// <summary>
    /// Triggers immediate confirmation email and SMS for the appointment described
    /// by <paramref name="request"/>.
    /// </summary>
    /// <param name="request">
    /// Appointment-context payload built from the committed booking data.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="BookingConfirmationNotificationResult"/> summarising the delivery
    /// outcomes for both channels.  Never throws.
    /// </returns>
    Task<BookingConfirmationNotificationResult> SendAsync(
        BookingConfirmationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// High-level outcome returned to callers of
/// <see cref="IBookingConfirmationNotificationService.SendAsync"/>.
/// </summary>
/// <param name="EmailSucceeded">Whether the confirmation email was dispatched.</param>
/// <param name="SmsSucceeded">Whether the confirmation SMS was dispatched.</param>
/// <param name="SmsOptedOut">
/// <c>true</c> when SMS was intentionally skipped because the patient opted out (AC-4).
/// Not counted as a failure.
/// </param>
/// <param name="SmsGatewayDisabled">
/// <c>true</c> when SMS was skipped because the gateway is disabled or credits are
/// exhausted (EC-1 / SMS-disabled mode).
/// </param>
/// <param name="PdfAttached">
/// <c>true</c> when the PDF confirmation was successfully generated and attached to the email.
/// </param>
/// <param name="PdfRetryNeeded">
/// <c>true</c> when PDF generation failed and a deferred retry has been logged (EC-1).
/// </param>
/// <param name="IsAlreadyCancelled">
/// <c>true</c> when the appointment was found to be already cancelled at the time
/// confirmation was processed (EC-2).
/// </param>
public sealed record BookingConfirmationNotificationResult(
    bool EmailSucceeded,
    bool SmsSucceeded,
    bool SmsOptedOut,
    bool SmsGatewayDisabled,
    bool PdfAttached,
    bool PdfRetryNeeded,
    bool IsAlreadyCancelled);
