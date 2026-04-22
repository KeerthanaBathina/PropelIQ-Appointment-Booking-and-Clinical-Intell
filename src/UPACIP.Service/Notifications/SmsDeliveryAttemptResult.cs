namespace UPACIP.Service.Notifications;

/// <summary>
/// Transport-level result returned by <see cref="ISmsTransport.SendAsync"/> after a
/// single delivery attempt (including any retries performed internally).
///
/// Downstream orchestration MUST inspect <see cref="Outcome"/> before persisting to
/// <c>NotificationLog</c> or deciding whether to fall through to email-only delivery.
/// </summary>
public sealed class SmsDeliveryAttemptResult
{
    // -------------------------------------------------------------------------
    // Factory helpers — prefer these over direct construction
    // -------------------------------------------------------------------------

    /// <summary>Creates a successful delivery result.</summary>
    /// <param name="twilioMessageSid">Twilio Message SID returned by the API (e.g. <c>SM…</c>).</param>
    /// <param name="attemptsMade">Total API call attempts (1 = succeeded on first try).</param>
    public static SmsDeliveryAttemptResult Succeeded(string twilioMessageSid, int attemptsMade) =>
        new(SmsDeliveryOutcome.Sent, twilioMessageSid, attemptsMade, null);

    /// <summary>
    /// Creates a result for permanently rejected international numbers (EC-2).
    /// This outcome is NOT retryable.
    /// </summary>
    /// <param name="phoneNumber">The rejected number (masked for log safety).</param>
    public static SmsDeliveryAttemptResult InvalidNumber(string phoneNumber) =>
        new(SmsDeliveryOutcome.InvalidNumber, null, 0,
            $"Phone number does not match required country code prefix (Phase 1: US +1 only).");

    /// <summary>
    /// Creates a gateway-disabled result when SMS is administratively turned off
    /// (e.g. trial credits exhausted — EC-1).
    /// </summary>
    public static SmsDeliveryAttemptResult GatewayDisabled() =>
        new(SmsDeliveryOutcome.GatewayDisabled, null, 0,
            "SMS gateway is disabled. Continuing with email-only notifications.");

    /// <summary>
    /// Creates a permanently failed result after all retry attempts were exhausted.
    /// </summary>
    /// <param name="attemptsMade">Total API call attempts made before giving up.</param>
    /// <param name="reason">Last error message (sanitised, no credentials or PII).</param>
    public static SmsDeliveryAttemptResult Failed(int attemptsMade, string reason) =>
        new(SmsDeliveryOutcome.Failed, null, attemptsMade, reason);

    // -------------------------------------------------------------------------
    // Properties
    // -------------------------------------------------------------------------

    /// <summary>Delivery outcome classification.</summary>
    public SmsDeliveryOutcome Outcome { get; }

    /// <summary>
    /// Twilio Message SID returned on success (e.g. <c>SMxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx</c>).
    /// <c>null</c> for all non-successful outcomes.
    /// </summary>
    public string? TwilioMessageSid { get; }

    /// <summary>
    /// Total number of API call attempts, including the initial attempt and retries.
    /// 0 for <see cref="SmsDeliveryOutcome.GatewayDisabled"/> and
    /// <see cref="SmsDeliveryOutcome.InvalidNumber"/> (no attempt was made).
    /// </summary>
    public int AttemptsMade { get; }

    /// <summary>
    /// Human-readable reason for a non-successful outcome.
    /// <c>null</c> on success.  Sanitised — MUST NOT contain credentials or patient PII.
    /// </summary>
    public string? FailureReason { get; }

    /// <summary>UTC timestamp when the attempt result was produced.</summary>
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;

    /// <summary>Convenience: <c>true</c> when <see cref="Outcome"/> is <see cref="SmsDeliveryOutcome.Sent"/>.</summary>
    public bool IsSuccess => Outcome == SmsDeliveryOutcome.Sent;

    /// <summary>Convenience: <c>true</c> when SMS was skipped because the gateway is disabled.</summary>
    public bool IsGatewayDisabled => Outcome == SmsDeliveryOutcome.GatewayDisabled;

    /// <summary>Convenience: <c>true</c> when the number was rejected due to scope restriction.</summary>
    public bool IsInvalidNumber => Outcome == SmsDeliveryOutcome.InvalidNumber;

    private SmsDeliveryAttemptResult(
        SmsDeliveryOutcome outcome,
        string? twilioMessageSid,
        int attemptsMade,
        string? failureReason)
    {
        Outcome          = outcome;
        TwilioMessageSid = twilioMessageSid;
        AttemptsMade     = attemptsMade;
        FailureReason    = failureReason;
    }
}

/// <summary>
/// Classifies the final outcome of a transport-level SMS delivery attempt.
/// </summary>
public enum SmsDeliveryOutcome
{
    /// <summary>The message was accepted by Twilio.</summary>
    Sent,

    /// <summary>
    /// Delivery failed after exhausting all retry attempts due to transient or
    /// unrecoverable Twilio API errors.
    /// </summary>
    Failed,

    /// <summary>
    /// The recipient phone number was rejected because it does not match the
    /// allowed country code (Phase 1: US <c>+1</c> only — EC-2).
    /// This outcome is NOT retryable.
    /// </summary>
    InvalidNumber,

    /// <summary>
    /// SMS gateway is administratively disabled (e.g. Twilio trial credits
    /// exhausted — EC-1).  Callers should fall through to email-only delivery.
    /// </summary>
    GatewayDisabled,
}
