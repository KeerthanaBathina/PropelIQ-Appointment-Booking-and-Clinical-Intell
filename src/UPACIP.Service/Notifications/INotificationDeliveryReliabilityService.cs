namespace UPACIP.Service.Notifications;

/// <summary>
/// Cross-channel queue interface exposed by <see cref="NotificationRetryWorker"/> so
/// scoped services can schedule a retry without a direct dependency on the hosted worker.
/// </summary>
public interface INotificationRetryQueue
{
    /// <summary>
    /// Adds <paramref name="request"/> to the in-memory retry schedule.
    /// The worker processes items when <see cref="NotificationRetryRequest.NextRetryAt"/> elapses.
    /// </summary>
    void EnqueueRetry(NotificationRetryRequest request);
}

/// <summary>
/// Orchestrates notification delivery reliability across email and SMS channels (US_037).
///
/// Responsibilities:
/// <list type="bullet">
///   <item>
///     Record every final provider outcome to <see cref="BufferedNotificationLogWriter"/>
///     so delivery attempts are observable even when transient persistence failures occur (AC-1, EC-1).
///   </item>
///   <item>
///     Schedule orchestration-level retries at 1 minute, 5 minutes, and 15 minutes after
///     a transient failure, keeping these separate from the short provider-level retries
///     already executed inside the transport layer (AC-2).
///   </item>
///   <item>
///     Mark the notification <c>PermanentlyFailed</c> after the third retry attempt fails
///     and emit a structured <c>LogWarning</c> for staff review (AC-3).
///   </item>
///   <item>
///     Treat bounced or invalid-recipient email outcomes as contact-quality events:
///     flag the patient record for staff review instead of entering the retry schedule (EC-2).
///   </item>
/// </list>
/// </summary>
public interface INotificationDeliveryReliabilityService
{
    /// <summary>
    /// Records the outcome of an email delivery attempt and applies the appropriate
    /// reliability action (retry schedule, permanent-failure marking, or bounce handling).
    /// </summary>
    Task HandleEmailOutcomeAsync(
        NotificationEmailRequest request,
        NotificationEmailResult  result,
        CancellationToken        ct = default);

    /// <summary>
    /// Records the outcome of an SMS delivery attempt and applies the appropriate
    /// reliability action (retry schedule or permanent-failure marking).
    /// </summary>
    Task HandleSmsOutcomeAsync(
        NotificationSmsRequest request,
        NotificationSmsResult  result,
        CancellationToken      ct = default);
}
