namespace UPACIP.Service.Notifications;

/// <summary>
/// Payload describing a completed slot swap, consumed by
/// <see cref="ISlotSwapNotificationService.SendSwapNotificationAsync"/> (US_036 AC-2).
///
/// The caller is responsible for supplying pre-formatted time strings so this service
/// remains agnostic to locale and time-zone concerns.
/// </summary>
/// <param name="AppointmentId">
/// FK to the <c>Appointment</c> that was moved.  Used as the anchor for
/// <c>notification_logs.appointment_id</c>.
/// </param>
/// <param name="PatientId">
/// FK to the patient for opt-out lookup and PII-safe log correlation.
/// </param>
/// <param name="PatientEmail">Recipient email address.</param>
/// <param name="PatientPhoneNumber">
/// Recipient E.164 phone number (e.g. <c>+12025551234</c>).
/// Empty when the patient has no number on file; SMS will be skipped.
/// </param>
/// <param name="PatientFullName">Full name for email and SMS personalisation.</param>
/// <param name="OldAppointmentTime">
/// UTC timestamp of the original appointment the patient was moved away from.
/// </param>
/// <param name="NewAppointmentTime">
/// UTC timestamp of the new appointment the patient was moved to.
/// </param>
/// <param name="OldTimeFormatted">
/// Human-readable string for the old appointment time (e.g. "April 22, 2026 at 9:00 AM").
/// Used directly in message bodies.
/// </param>
/// <param name="NewTimeFormatted">
/// Human-readable string for the new appointment time.
/// Used directly in message bodies.
/// </param>
/// <param name="ProviderName">Provider display name at the new appointment; null for walk-in.</param>
/// <param name="AppointmentType">Category label (e.g. "General Checkup"); null if unknown.</param>
/// <param name="BookingReference">
/// Updated booking reference if one exists; null for legacy / walk-in records.
/// </param>
/// <param name="CorrelationId">
/// Optional trace identifier for structured-log correlation across the swap pipeline.
/// </param>
public sealed record SlotSwapNotificationRequest(
    Guid AppointmentId,
    Guid PatientId,
    string PatientEmail,
    string PatientPhoneNumber,
    string PatientFullName,
    DateTime OldAppointmentTime,
    DateTime NewAppointmentTime,
    string OldTimeFormatted,
    string NewTimeFormatted,
    string? ProviderName,
    string? AppointmentType,
    string? BookingReference = null,
    string? CorrelationId = null);

/// <summary>
/// Per-channel delivery outcome returned by
/// <see cref="ISlotSwapNotificationService.SendSwapNotificationAsync"/>.
/// </summary>
/// <param name="EmailSent">
/// <c>true</c> when the email was accepted by the SMTP provider.
/// </param>
/// <param name="SmsSent">
/// <c>true</c> when the SMS was accepted by Twilio.
/// </param>
/// <param name="SmsSkippedOptOut">
/// <c>true</c> when SMS was skipped because the patient opted out.
/// This is NOT a failure — notification was delivered via email.
/// </param>
/// <param name="EmailFailed">
/// <c>true</c> when the email delivery failed (transient or bounce).
/// The swap is NOT rolled back; only the delivery is unsuccessful.
/// </param>
public sealed record SlotSwapNotificationResult(
    bool EmailSent,
    bool SmsSent,
    bool SmsSkippedOptOut,
    bool EmailFailed);
