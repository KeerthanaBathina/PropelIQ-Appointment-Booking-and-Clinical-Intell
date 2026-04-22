using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Payload describing a pending orchestration-level retry scheduled by
/// <see cref="INotificationDeliveryReliabilityService"/> (US_037 AC-2).
///
/// The <see cref="NotificationRetryWorker"/> holds these records in an in-memory queue
/// and re-dispatches the appropriate channel service when <see cref="NextRetryAt"/> elapses.
/// </summary>
/// <param name="AppointmentId">FK to the appointment this notification concerns.</param>
/// <param name="PatientId">FK to the patient recipient.</param>
/// <param name="PatientEmail">Recipient email address (used when Channel is Email).</param>
/// <param name="PatientPhoneNumber">Recipient E.164 phone number (used when Channel is Sms).</param>
/// <param name="PatientName">Display name for personalisation.</param>
/// <param name="Channel">Delivery channel that failed and is being retried.</param>
/// <param name="NotificationType">Event type driving the original notification.</param>
/// <param name="AppointmentTime">Scheduled UTC appointment time for body composition.</param>
/// <param name="ProviderName">Assigned provider display name, null for walk-ins.</param>
/// <param name="AppointmentType">Appointment category, null when not specified.</param>
/// <param name="BookingReference">Short booking reference, null for walk-ins.</param>
/// <param name="CancellationLink">Prefilled cancellation URL (email Confirmation type only).</param>
/// <param name="AttemptNumber">
/// 1-based orchestration retry sequence.  Backoff schedule:
///   1 → +1 minute, 2 → +5 minutes, 3 → +15 minutes (final attempt).
/// </param>
/// <param name="NextRetryAt">UTC timestamp when this retry becomes eligible for processing.</param>
/// <param name="CorrelationId">Trace identifier for log correlation.</param>
public sealed record NotificationRetryRequest(
    Guid AppointmentId,
    Guid PatientId,
    string PatientEmail,
    string PatientPhoneNumber,
    string PatientName,
    DeliveryChannel Channel,
    NotificationType NotificationType,
    DateTime AppointmentTime,
    string? ProviderName,
    string? AppointmentType,
    string? BookingReference,
    string? CancellationLink,
    int AttemptNumber,
    DateTime NextRetryAt,
    string? CorrelationId = null);
