using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Per-row projection returned by the notification-log admin endpoint.
/// Contains all fields needed for operational inspection and follow-up (US_037 AC-1).
/// </summary>
/// <param name="NotificationId">Unique notification log identifier.</param>
/// <param name="AppointmentId">FK to the related appointment.</param>
/// <param name="NotificationType">Event type that triggered the notification.</param>
/// <param name="Channel">Delivery channel used.</param>
/// <param name="RecipientAddress">
/// Email address or E.164 phone number.  Null for rows created before US_037.
/// </param>
/// <param name="Status">Current lifecycle status of this notification.</param>
/// <param name="RetryCount">Number of orchestration-level retries attempted.</param>
/// <param name="SentAt">UTC timestamp of successful delivery. Null if not yet delivered.</param>
/// <param name="FinalAttemptAt">UTC timestamp of the most recent attempt (initial or retry).</param>
/// <param name="IsStaffReviewRequired">Whether this notification needs staff follow-up.</param>
/// <param name="IsContactValidationRequired">Whether a bounce flagged the patient for contact validation.</param>
/// <param name="CreatedAt">UTC timestamp when this log record was created.</param>
/// <param name="AttemptCount">Total number of per-attempt rows linked to this log record.</param>
public sealed record NotificationLogRowDto(
    Guid             NotificationId,
    Guid             AppointmentId,
    NotificationType NotificationType,
    DeliveryChannel  Channel,
    string?          RecipientAddress,
    NotificationStatus Status,
    int              RetryCount,
    DateTime?        SentAt,
    DateTime?        FinalAttemptAt,
    bool             IsStaffReviewRequired,
    bool             IsContactValidationRequired,
    DateTime         CreatedAt,
    int              AttemptCount);

/// <summary>
/// Paginated result page returned by the notification-log admin endpoint.
/// </summary>
/// <param name="Items">Notification log rows for the current page.</param>
/// <param name="TotalCount">Total number of rows matching the applied filters.</param>
/// <param name="Page">Current 1-based page index.</param>
/// <param name="PageSize">Number of rows per page.</param>
public sealed record NotificationLogPageDto(
    IReadOnlyList<NotificationLogRowDto> Items,
    int TotalCount,
    int Page,
    int PageSize);

/// <summary>
/// Aggregate delivery statistics summary returned alongside or instead of the row list
/// (US_037 AC-4).
///
/// Statistics are computed exclusively over <em>attempted</em> notifications.
/// <see cref="NotificationStatus.OptedOut"/> and <see cref="NotificationStatus.CancelledBeforeSend"/>
/// are excluded from the denominator and from failure counts (EC-1).
/// </summary>
/// <param name="TotalAttempted">
/// Count of notification rows with an attempted-delivery status
/// (i.e. excluding <c>OptedOut</c> and <c>CancelledBeforeSend</c>).
/// </param>
/// <param name="TotalSent">Count of rows with <see cref="NotificationStatus.Sent"/>.</param>
/// <param name="TotalFailed">
/// Count of rows with <see cref="NotificationStatus.Failed"/> or
/// <see cref="NotificationStatus.PermanentlyFailed"/> or
/// <see cref="NotificationStatus.Bounced"/>.
/// </param>
/// <param name="TotalPermanentlyFailed">
/// Count of rows marked <see cref="NotificationStatus.PermanentlyFailed"/> requiring staff review.
/// </param>
/// <param name="TotalOptedOut">
/// Count of rows skipped because the patient opted out of the channel (informational only).
/// </param>
/// <param name="TotalCancelledBeforeSend">
/// Count of rows skipped because the appointment was cancelled before dispatch (informational only).
/// </param>
/// <param name="SuccessRatePct">
/// Percentage of attempted deliveries that succeeded.
/// <c>null</c> when <see cref="TotalAttempted"/> is zero.
/// </param>
/// <param name="FailureRatePct">
/// Percentage of attempted deliveries that failed (any non-sent outcome).
/// <c>null</c> when <see cref="TotalAttempted"/> is zero.
/// </param>
/// <param name="AverageDeliveryTimeMs">
/// Average round-trip delivery duration in milliseconds, derived from
/// <see cref="UPACIP.DataAccess.Entities.NotificationDeliveryAttempt.DurationMs"/>
/// across successful attempts.  <c>null</c> when no successful attempt has a duration recorded.
/// </param>
/// <param name="StaffReviewPending">
/// Count of <c>permanently_failed</c> rows still awaiting staff follow-up.
/// </param>
public sealed record NotificationLogSummaryDto(
    int     TotalAttempted,
    int     TotalSent,
    int     TotalFailed,
    int     TotalPermanentlyFailed,
    int     TotalOptedOut,
    int     TotalCancelledBeforeSend,
    double? SuccessRatePct,
    double? FailureRatePct,
    double? AverageDeliveryTimeMs,
    int     StaffReviewPending);
