using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Appointment-context payload consumed by
/// <see cref="IBookingConfirmationNotificationService"/> to coordinate immediate
/// confirmation email and SMS dispatch after a successful booking.
///
/// Callers (the booking service) populate this record from the committed
/// <c>Appointment</c> and <c>Patient</c> data so the notification layer has all
/// context it needs without performing additional DB reads.
/// </summary>
/// <param name="AppointmentId">PK of the newly committed appointment.</param>
/// <param name="PatientId">PK of the patient — used for SMS opt-out lookup.</param>
/// <param name="PatientEmail">Recipient email address for the confirmation email.</param>
/// <param name="PatientPhoneNumber">
/// Recipient E.164 phone number for the confirmation SMS (e.g. <c>+12025551234</c>).
/// Phase 1 supports US numbers only.  SMS is skipped silently if the number is empty
/// or outside the allowed country code scope.
/// </param>
/// <param name="PatientName">Full name used for personalisation in both channels.</param>
/// <param name="AppointmentTime">Scheduled UTC appointment time.</param>
/// <param name="ProviderName">Display name of the assigned provider (e.g. "Dr. Jane Smith").</param>
/// <param name="AppointmentType">Category of the appointment (e.g. "General Checkup").</param>
/// <param name="BookingReference">
/// Short booking reference printed in the confirmation (e.g. "BK-20260422-X7R2KP").
/// </param>
/// <param name="CancellationBaseUrl">
/// Base URL of the portal's cancellation route (e.g. <c>https://portal.upacip.clinic</c>).
/// A prefilled cancellation link is constructed by appending the appointment ID so the
/// patient can cancel directly from the email (AC-3).
/// </param>
/// <param name="CorrelationId">
/// Optional trace identifier propagated from the booking HTTP context for log correlation.
/// </param>
public sealed record BookingConfirmationRequest(
    Guid AppointmentId,
    Guid PatientId,
    string PatientEmail,
    string PatientPhoneNumber,
    string PatientName,
    DateTime AppointmentTime,
    string? ProviderName,
    string? AppointmentType,
    string BookingReference,
    string CancellationBaseUrl,
    string? CorrelationId = null);
