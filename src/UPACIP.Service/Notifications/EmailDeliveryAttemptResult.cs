namespace UPACIP.Service.Notifications;

/// <summary>
/// Transport-level result returned by <see cref="IEmailTransport.SendAsync"/> after a
/// single delivery attempt (including any retries performed by the transport itself).
///
/// Downstream orchestration layers MUST inspect <see cref="Outcome"/> before deciding
/// whether to retry, log a failure, or flag the patient record for contact update.
/// </summary>
public sealed class EmailDeliveryAttemptResult
{
    // -------------------------------------------------------------------------
    // Factory helpers — prefer these over direct construction
    // -------------------------------------------------------------------------

    /// <summary>Creates a successful delivery result.</summary>
    /// <param name="providerName">Name of the provider that accepted the message.</param>
    /// <param name="attemptsMade">Total send attempts consumed (1 = first try succeeded).</param>
    public static EmailDeliveryAttemptResult Succeeded(string providerName, int attemptsMade) =>
        new(EmailDeliveryOutcome.Sent, providerName, attemptsMade, null, usedFallback: false);

    /// <summary>
    /// Creates a successful delivery result that used the fallback provider after
    /// the primary provider exhausted its quota or became unavailable (EC-1).
    /// </summary>
    /// <param name="fallbackProviderName">Name of the fallback provider used.</param>
    /// <param name="attemptsMade">Total attempts across primary + fallback.</param>
    public static EmailDeliveryAttemptResult SucceededViaFallback(
        string fallbackProviderName,
        int attemptsMade) =>
        new(EmailDeliveryOutcome.Sent, fallbackProviderName, attemptsMade, null, usedFallback: true);

    /// <summary>
    /// Creates a bounced result for invalid or non-existent recipient addresses.
    /// A bounce is NOT retryable — the recipient address itself is the problem (EC-2).
    /// </summary>
    /// <param name="providerName">Provider that reported the bounce.</param>
    /// <param name="reason">Bounce reason as reported by the remote MTA (sanitised, no PII).</param>
    public static EmailDeliveryAttemptResult Bounced(string providerName, string reason) =>
        new(EmailDeliveryOutcome.Bounced, providerName, 1, reason, usedFallback: false);

    /// <summary>
    /// Creates a permanently failed result after all retry attempts were exhausted.
    /// </summary>
    /// <param name="providerName">Last provider attempted.</param>
    /// <param name="attemptsMade">Total send attempts made before giving up.</param>
    /// <param name="reason">Last error message (sanitised, no credentials or PII).</param>
    public static EmailDeliveryAttemptResult Failed(
        string providerName,
        int attemptsMade,
        string reason) =>
        new(EmailDeliveryOutcome.Failed, providerName, attemptsMade, reason, usedFallback: false);

    // -------------------------------------------------------------------------
    // Properties
    // -------------------------------------------------------------------------

    /// <summary>Delivery outcome classification.</summary>
    public EmailDeliveryOutcome Outcome { get; }

    /// <summary>
    /// Name of the SMTP provider that produced this result (e.g. <c>"SendGrid"</c>,
    /// <c>"Gmail"</c>).  Used for structured diagnostics; never contains credentials.
    /// </summary>
    public string ProviderName { get; }

    /// <summary>
    /// Total number of send attempts consumed by the transport for this message,
    /// including the initial attempt and any retries.
    /// </summary>
    public int AttemptsMade { get; }

    /// <summary>
    /// Human-readable reason for a <see cref="EmailDeliveryOutcome.Bounced"/> or
    /// <see cref="EmailDeliveryOutcome.Failed"/> outcome.  <c>null</c> on success.
    /// Sanitised — MUST NOT contain SMTP passwords, API keys, or patient PII.
    /// </summary>
    public string? FailureReason { get; }

    /// <summary>
    /// <c>true</c> when the delivery was completed by the fallback provider after
    /// the primary provider's quota or availability was exhausted (EC-1).
    /// </summary>
    public bool UsedFallback { get; }

    /// <summary>UTC timestamp when the attempt result was produced.</summary>
    public DateTimeOffset Timestamp { get; } = DateTimeOffset.UtcNow;

    /// <summary>Convenience: <c>true</c> when <see cref="Outcome"/> is <see cref="EmailDeliveryOutcome.Sent"/>.</summary>
    public bool IsSuccess => Outcome == EmailDeliveryOutcome.Sent;

    /// <summary>Convenience: <c>true</c> when <see cref="Outcome"/> is <see cref="EmailDeliveryOutcome.Bounced"/>.</summary>
    public bool IsBounced => Outcome == EmailDeliveryOutcome.Bounced;

    private EmailDeliveryAttemptResult(
        EmailDeliveryOutcome outcome,
        string providerName,
        int attemptsMade,
        string? failureReason,
        bool usedFallback)
    {
        Outcome       = outcome;
        ProviderName  = providerName;
        AttemptsMade  = attemptsMade;
        FailureReason = failureReason;
        UsedFallback  = usedFallback;
    }
}

/// <summary>
/// Classifies the final outcome of a transport-level delivery attempt.
/// </summary>
public enum EmailDeliveryOutcome
{
    /// <summary>The message was accepted by the remote SMTP server.</summary>
    Sent,

    /// <summary>
    /// The message was permanently rejected because the recipient address is invalid
    /// or does not exist (5xx SMTP permanent failure or explicit bounce notification).
    /// Downstream: flag the patient record for contact update (EC-2).
    /// </summary>
    Bounced,

    /// <summary>
    /// Delivery failed after exhausting all retry attempts due to transient or
    /// unrecoverable transport errors.  Does NOT include bounces.
    /// </summary>
    Failed,
}
