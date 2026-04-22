using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Appointment-context payload consumed by <see cref="INotificationSmsService"/>
/// to compose and dispatch a notification SMS via the Twilio transport.
///
/// Callers (booking confirmation, reminders, slot-swap, waitlist offer) populate
/// this record from their appointment context and pass it to the notification
/// service without knowing which SMS provider is used.
/// </summary>
/// <param name="AppointmentId">FK to the <c>Appointment</c> being notified about.</param>
/// <param name="PatientId">FK used to resolve the patient's SMS opt-out preference before sending.</param>
/// <param name="PatientPhoneNumber">
/// Recipient E.164 phone number (e.g. <c>+12025551234</c>).
/// Phase 1 supports US numbers only (<c>+1</c> prefix); other prefixes result in a
/// deterministic <c>InvalidNumber</c> outcome without invoking the Twilio API.
/// </param>
/// <param name="PatientName">Full name used for personalisation in the SMS body.</param>
/// <param name="AppointmentTime">
/// Scheduled UTC appointment time.  The service localises this using the clinic's
/// configured IANA time zone for the SMS body text.
/// </param>
/// <param name="ProviderName">
/// Display name of the assigned provider (e.g. "Dr. Jane Smith").
/// Included in reminder SMS bodies when provided.
/// </param>
/// <param name="AppointmentType">
/// Category of the appointment (e.g. "General Checkup").
/// Currently reserved for future SMS template enrichment.
/// </param>
/// <param name="NotificationType">
/// The event driving this notification.  Determines which SMS template text is
/// composed and which <c>NotificationLog.NotificationType</c> value is stored.
/// </param>
/// <param name="BookingReference">
/// Short booking reference appended to confirmation SMS (e.g. "BK-20260422-X7R2KP").
/// Null for walk-in appointments or legacy records.
/// </param>
/// <param name="CorrelationId">
/// Optional trace identifier propagated from the calling HTTP context for log correlation.
/// </param>
public sealed record NotificationSmsRequest(
    Guid AppointmentId,
    Guid PatientId,
    string PatientPhoneNumber,
    string PatientName,
    DateTime AppointmentTime,
    string? ProviderName,
    string? AppointmentType,
    NotificationType NotificationType,
    string? BookingReference = null,
    string? CorrelationId = null);
