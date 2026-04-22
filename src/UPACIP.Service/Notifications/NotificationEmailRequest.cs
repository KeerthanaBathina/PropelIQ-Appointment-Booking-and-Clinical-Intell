using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Appointment-context payload consumed by <see cref="INotificationEmailService"/>
/// to compose and dispatch a notification email.
///
/// Callers (booking confirmation, reminders, slot-swap, waitlist offer) populate
/// this record from their appointment context and pass it to the notification
/// service without knowing which SMTP provider is used.
/// </summary>
/// <param name="AppointmentId">FK to the <c>Appointment</c> being notified about.</param>
/// <param name="PatientId">FK to the patient receiving the email.</param>
/// <param name="PatientEmail">Recipient email address.</param>
/// <param name="PatientName">Full name used for personalisation in the email body.</param>
/// <param name="AppointmentTime">
/// Scheduled UTC appointment time.  The service formats this using the clinic's
/// configured IANA time zone for the email body.
/// </param>
/// <param name="ProviderName">
/// Display name of the assigned provider (e.g. "Dr. Jane Smith").
/// Null or empty when no provider is assigned (e.g. walk-in).
/// </param>
/// <param name="AppointmentType">
/// Category of the appointment (e.g. "General Checkup").
/// Null or empty when not specified.
/// </param>
/// <param name="NotificationType">
/// The event driving this notification.  Determines which template is rendered
/// and which <c>NotificationLog.NotificationType</c> value is stored.
/// </param>
/// <param name="BookingReference">
/// Short booking reference displayed to the patient (e.g. "BK-20260422-X7R2KP").
/// Null for walk-in appointments or legacy records.
/// </param>
/// <param name="CancellationLink">
/// Prefilled cancellation URL to include in booking confirmation emails (AC-3).
/// The link encodes the appointment ID so the patient can cancel in one click.
/// Null for notification types other than <see cref="UPACIP.DataAccess.Enums.NotificationType.Confirmation"/>.
/// </param>
/// <param name="IsAlreadyCancelled">
/// When <c>true</c> the appointment was already cancelled before the confirmation
/// was composed.  The email template displays a cancellation notice instead of
/// scheduled-appointment language (EC-2).
/// </param>
/// <param name="CorrelationId">
/// Optional trace identifier propagated from the calling HTTP context for log correlation.
/// </param>
public sealed record NotificationEmailRequest(
    Guid AppointmentId,
    Guid PatientId,
    string PatientEmail,
    string PatientName,
    DateTime AppointmentTime,
    string? ProviderName,
    string? AppointmentType,
    NotificationType NotificationType,
    string? BookingReference = null,
    string? CancellationLink = null,
    bool IsAlreadyCancelled = false,
    string? CorrelationId = null);
