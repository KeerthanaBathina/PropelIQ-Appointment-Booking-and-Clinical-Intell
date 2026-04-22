namespace UPACIP.Service.Notifications;

/// <summary>
/// Slot-availability and candidate context payload for a waitlist offer notification
/// (US_036 AC-1, AC-3).
///
/// Carries all context the notification service needs to compose and dispatch email
/// and SMS offer messages without reaching back into the database.  The caller
/// (<see cref="UPACIP.Service.Appointments.WaitlistService"/>) is responsible for
/// building the claim link and resolving human-readable appointment details before
/// constructing this record.
/// </summary>
/// <param name="WaitlistEntryId">
/// <see cref="UPACIP.DataAccess.Entities.WaitlistEntry.Id"/> of the candidate being notified.
/// Used for structured log correlation only — not sent to the patient.
/// </param>
/// <param name="PatientId">
/// <see cref="UPACIP.DataAccess.Entities.Patient.Id"/> for opt-out lookup.
/// </param>
/// <param name="PatientEmail">Recipient email address.</param>
/// <param name="PatientPhoneNumber">
/// Recipient E.164 phone number (e.g. <c>+12025551234</c>).  May be empty when the
/// patient has no phone number on file; the SMS channel will be skipped in that case.
/// </param>
/// <param name="PatientFullName">Full name for personalisation in the message bodies.</param>
/// <param name="SlotId">Stable slot identifier (date + time + providerId composite).</param>
/// <param name="AppointmentDetails">
/// Human-readable summary for the message body, e.g.
/// <c>"April 23, 2026 at 10:00 AM with Dr. Jane Smith"</c>.
/// </param>
/// <param name="AppointmentTimeUtc">UTC appointment start time used for secondary formatting.</param>
/// <param name="ProviderName">Provider display name; null for walk-in / any-provider slots.</param>
/// <param name="AppointmentType">Appointment category label, e.g. <c>"General Checkup"</c>.</param>
/// <param name="ClaimLink">
/// Full booking claim URL the patient must visit within 24 hours to reserve the slot
/// (e.g. <c>https://portal.example.com/book?claim=TOKEN</c>).
/// </param>
/// <param name="IsWithin24Hours">
/// <c>true</c> when the offered slot starts within 24 hours of dispatch.
/// The email and SMS templates add urgency copy in this case.
/// </param>
/// <param name="CorrelationId">
/// Optional trace identifier propagated from the calling context for structured-log correlation.
/// </param>
public sealed record WaitlistOfferNotificationRequest(
    Guid WaitlistEntryId,
    Guid PatientId,
    string PatientEmail,
    string PatientPhoneNumber,
    string PatientFullName,
    string SlotId,
    string AppointmentDetails,
    DateTime AppointmentTimeUtc,
    string? ProviderName,
    string AppointmentType,
    string ClaimLink,
    bool IsWithin24Hours,
    string? CorrelationId = null);

/// <summary>
/// Outcome of a single waitlist offer notification dispatch attempt.
/// Returned by <see cref="IWaitlistOfferNotificationService.SendOfferAsync"/>.
/// </summary>
/// <param name="EmailSent"><c>true</c> when the offer email was accepted by the SMTP provider.</param>
/// <param name="SmsSent"><c>true</c> when the offer SMS was accepted by Twilio.</param>
/// <param name="SmsSkippedOptOut"><c>true</c> when SMS was skipped because the patient opted out.</param>
/// <param name="IsInvalidContact">
/// <c>true</c> when the email bounced (permanent rejection) AND the SMS was rejected as an
/// invalid number (or phone number is absent).  When both channels fail with invalid-contact
/// outcomes the caller should skip this candidate and advance to the next (EC-1).
/// </param>
public sealed record WaitlistOfferNotificationResult(
    bool EmailSent,
    bool SmsSent,
    bool SmsSkippedOptOut,
    bool IsInvalidContact);
