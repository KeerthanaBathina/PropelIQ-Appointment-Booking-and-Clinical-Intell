using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Appointments;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Orchestrates appointment-event SMS delivery.
///
/// Responsibilities:
/// <list type="bullet">
///   <item>
///     Consult <c>Patient.SmsOptedOut</c> before any transport invocation and record
///     <c>OptedOut</c> as a first-class outcome when the preference is set (AC-2, EC-2).
///   </item>
///   <item>Compose a concise, localised SMS body from the appointment context.</item>
///   <item>
///     Invoke <see cref="ISmsTransport"/> (Twilio) to deliver the composed message
///     within the 30-second delivery window (AC-1, AC-3).
///   </item>
///   <item>
///     Persist a <c>NotificationLog</c> row with the final outcome status (Sent /
///     Failed / OptedOut), retry count, delivery timestamp, and channel (AC-4).
///   </item>
///   <item>
///     When the SMS gateway is disabled or Twilio trial credits are exhausted, emit a
///     <c>LogCritical</c> operational alert and return a result that signals email-only
///     mode to the caller (EC-1).
///   </item>
/// </list>
///
/// This service never throws; all outcomes are encoded in <see cref="NotificationSmsResult"/>.
/// </summary>
public sealed class NotificationSmsService : INotificationSmsService
{
    private readonly ApplicationDbContext            _db;
    private readonly ISmsTransport                   _transport;
    private readonly ClinicSettings                  _clinicSettings;
    private readonly ILogger<NotificationSmsService> _logger;

    public NotificationSmsService(
        ApplicationDbContext             db,
        ISmsTransport                    transport,
        ClinicSettings                   clinicSettings,
        ILogger<NotificationSmsService> logger)
    {
        _db             = db;
        _transport      = transport;
        _clinicSettings = clinicSettings;
        _logger         = logger;
    }

    /// <inheritdoc/>
    public async Task<NotificationSmsResult> SendAsync(
        NotificationSmsRequest request,
        CancellationToken cancellationToken = default)
    {
        // ── 1. Consult patient SMS opt-out preference before any transport call ──
        //    (AC-2, EC-2) — opt-out is a first-class outcome, NOT a failure.
        bool smsOptedOut;
        try
        {
            smsOptedOut = await _db.Patients
                .Where(p => p.Id == request.PatientId)
                .Select(p => p.SmsOptedOut)
                .FirstOrDefaultAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Fail closed: if the preference cannot be read, abort and record Failed.
            // Logging a Critical here would be over-escalation — it's a transient DB issue.
            _logger.LogError(ex,
                "Failed to read SMS opt-out preference for patient {PatientId} " +
                "(appointment {AppointmentId}). Aborting SMS delivery as safe default. " +
                "Correlation: {CorrelationId}",
                request.PatientId, request.AppointmentId, request.CorrelationId ?? "N/A");

            await PersistLogAsync(request, NotificationStatus.Failed, 0, null, cancellationToken);

            return new NotificationSmsResult(
                Succeeded: false, IsOptedOut: false, IsGatewayDisabled: false,
                IsInvalidNumber: false, AttemptsMade: 0);
        }

        if (smsOptedOut)
        {
            _logger.LogInformation(
                "SMS skipped for appointment {AppointmentId} — patient {PatientId} has opted out. " +
                "Recording opted-out outcome in NotificationLog. Correlation: {CorrelationId}",
                request.AppointmentId, request.PatientId, request.CorrelationId ?? "N/A");

            await PersistLogAsync(request, NotificationStatus.OptedOut, 0, null, cancellationToken);

            return new NotificationSmsResult(
                Succeeded: false, IsOptedOut: true, IsGatewayDisabled: false,
                IsInvalidNumber: false, AttemptsMade: 0);
        }

        // ── 2. Compose SMS body ───────────────────────────────────────────────
        var body = BuildSmsBody(request, _clinicSettings.Name, _clinicSettings.TimeZoneId);

        // ── 3. Invoke transport (retries + timeout enforced inside transport) ──
        var transportMsg    = new SmsTransportMessage(request.PatientPhoneNumber, body);
        var deliveryResult  = await _transport.SendAsync(transportMsg, cancellationToken);

        // ── 4. Handle gateway-disabled / credits exhausted (EC-1) ─────────────
        if (deliveryResult.IsGatewayDisabled)
        {
            _logger.LogCritical(
                "[ADMIN ALERT] EP-005 SMS for appointment {AppointmentId} (type: {Type}) " +
                "could not be delivered — SMS gateway is disabled or Twilio trial credits are " +
                "exhausted.  Notification workflow continues in email-only mode. " +
                "Correlation: {CorrelationId}",
                request.AppointmentId, request.NotificationType, request.CorrelationId ?? "N/A");

            await PersistLogAsync(request, NotificationStatus.Failed, 0, null, cancellationToken);

            return new NotificationSmsResult(
                Succeeded: false, IsOptedOut: false, IsGatewayDisabled: true,
                IsInvalidNumber: false, AttemptsMade: 0);
        }

        // ── 5. Map transport outcome to persistence status (AC-4) ─────────────
        var retryCount           = Math.Max(0, deliveryResult.AttemptsMade - 1);
        var (logStatus, sentAt)  = deliveryResult.Outcome switch
        {
            SmsDeliveryOutcome.Sent => (NotificationStatus.Sent, (DateTime?)DateTime.UtcNow),
            _                      => (NotificationStatus.Failed, (DateTime?)null),
        };

        // ── 6. Persist outcome to NotificationLog ─────────────────────────────
        await PersistLogAsync(request, logStatus, retryCount, sentAt, cancellationToken);

        // ── 7. Structured diagnostics ─────────────────────────────────────────
        if (deliveryResult.IsSuccess)
        {
            _logger.LogInformation(
                "Notification SMS {Type} for appointment {AppointmentId} sent via Twilio " +
                "in {Attempts} attempt(s). SID: {Sid}. Correlation: {CorrelationId}",
                request.NotificationType, request.AppointmentId,
                deliveryResult.AttemptsMade, deliveryResult.TwilioMessageSid,
                request.CorrelationId ?? "N/A");
        }
        else if (deliveryResult.IsInvalidNumber)
        {
            _logger.LogWarning(
                "Notification SMS {Type} for appointment {AppointmentId} rejected — " +
                "phone number is outside the allowed country code scope (Phase 1: US +1 only). " +
                "Patient {PatientId}. Correlation: {CorrelationId}",
                request.NotificationType, request.AppointmentId,
                request.PatientId, request.CorrelationId ?? "N/A");
        }
        else
        {
            _logger.LogError(
                "Notification SMS {Type} for appointment {AppointmentId} failed after " +
                "{Attempts} attempt(s): {Reason}. Correlation: {CorrelationId}",
                request.NotificationType, request.AppointmentId,
                deliveryResult.AttemptsMade, deliveryResult.FailureReason,
                request.CorrelationId ?? "N/A");
        }

        return new NotificationSmsResult(
            Succeeded:        deliveryResult.IsSuccess,
            IsOptedOut:       false,
            IsGatewayDisabled: false,
            IsInvalidNumber:  deliveryResult.IsInvalidNumber,
            AttemptsMade:     deliveryResult.AttemptsMade,
            TwilioMessageSid: deliveryResult.TwilioMessageSid);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Appends a <see cref="NotificationLog"/> row capturing the SMS delivery outcome.
    /// Each result (including opt-out and retried failures) creates a new row —
    /// the entity is append-only.
    /// </summary>
    private async Task PersistLogAsync(
        NotificationSmsRequest request,
        NotificationStatus     status,
        int                    retryCount,
        DateTime?              sentAt,
        CancellationToken      ct)
    {
        try
        {
            _db.NotificationLogs.Add(new NotificationLog
            {
                NotificationId   = Guid.NewGuid(),
                AppointmentId    = request.AppointmentId,
                NotificationType = request.NotificationType,
                DeliveryChannel  = DeliveryChannel.Sms,
                Status           = status,
                RetryCount       = retryCount,
                SentAt           = sentAt,
                CreatedAt        = DateTime.UtcNow,
            });

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Log but do not propagate — a persistence failure must not hide the
            // delivery outcome from the caller.
            _logger.LogError(ex,
                "Failed to persist NotificationLog for appointment {AppointmentId} " +
                "(channel: SMS, status: {Status}).",
                request.AppointmentId, status);
        }
    }

    /// <summary>
    /// Composes a concise SMS body text for the given notification context.
    /// Each variant is designed to fit within a single 160-character SMS segment.
    /// </summary>
    private static string BuildSmsBody(
        NotificationSmsRequest request,
        string clinicName,
        string timeZoneId)
    {
        var local    = ConvertToLocalTime(request.AppointmentTime, timeZoneId);
        var dateStr  = local.ToString("ddd MMM d 'at' h:mm tt", CultureInfo.InvariantCulture);

        var refPart      = string.IsNullOrWhiteSpace(request.BookingReference)
            ? string.Empty
            : $" Ref: {request.BookingReference}.";

        var providerPart = string.IsNullOrWhiteSpace(request.ProviderName)
            ? string.Empty
            : $" With {request.ProviderName}.";

        return request.NotificationType switch
        {
            NotificationType.Confirmation =>
                $"{clinicName}: Appointment confirmed {dateStr}.{refPart} Reply STOP to opt out.",

            NotificationType.Reminder24h =>
                $"{clinicName}: Reminder - appointment tomorrow {dateStr}.{providerPart} Reply STOP to opt out.",

            NotificationType.Reminder2h =>
                $"{clinicName}: Reminder - appointment today {dateStr}.{providerPart} Reply STOP to opt out.",

            NotificationType.WaitlistOffer =>
                $"{clinicName}: A slot opened {dateStr}. Visit the portal to confirm. Reply STOP to opt out.",

            NotificationType.SlotSwapCompleted =>
                $"{clinicName}: Your appointment has been moved to {dateStr}. Reply STOP to opt out.",

            NotificationType.SlotSwapManualConfirmation =>
                $"{clinicName}: A preferred slot is available {dateStr}. Confirm at the portal. Reply STOP to opt out.",

            _ =>
                $"{clinicName}: Appointment notification for {dateStr}. Reply STOP to opt out.",
        };
    }

    /// <summary>
    /// Converts a UTC <see cref="DateTime"/> to the clinic's local time using the IANA
    /// timezone ID.  Falls back to UTC if the timezone ID is unresolvable.
    /// </summary>
    private static DateTime ConvertToLocalTime(DateTime utcTime, string timeZoneId)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return TimeZoneInfo.ConvertTimeFromUtc(utcTime, tz);
        }
        catch
        {
            return utcTime;
        }
    }
}
