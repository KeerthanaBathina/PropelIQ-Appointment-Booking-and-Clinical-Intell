using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Appointments;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Production implementation of <see cref="IReminderNotificationService"/>.
///
/// Routes each reminder batch work item to the correct channels and applies all
/// skip/deduplication rules before invoking the underlying transport services:
///
/// <list type="number">
///   <item><b>Cancelled-before-send (AC-5):</b> Re-reads appointment status from the
///     database immediately before dispatch.  If cancelled, logs
///     <c>CancelledBeforeSend</c> to <c>NotificationLog</c> for both channels and
///     returns without invoking any transport.</item>
///   <item><b>Deduplication (EC-1):</b> Checks <c>NotificationLog</c> for an existing
///     <c>Sent</c> record per channel + notification type before dispatching so a
///     checkpoint-resumed run never double-sends a channel that already succeeded.</item>
///   <item><b>24-hour batch (AC-1):</b> Sends personalized email (with cancellation
///     link) + SMS.  SMS is skipped with <c>OptedOut</c> when the patient has opted
///     out (AC-3).</item>
///   <item><b>2-hour batch (AC-2):</b> Sends SMS only.  Email is not dispatched for
///     the 2-hour window.</item>
///   <item><b>Cancellation link (EC-2):</b> Uses the existing
///     <c>{PortalBaseUrl}/appointments/{id}/cancel</c> contract, matching the
///     confirmation notification path.</item>
/// </list>
///
/// This service never throws.  All outcomes are encoded in
/// <see cref="ReminderNotificationResult"/>.
/// </summary>
public sealed class ReminderNotificationService : IReminderNotificationService
{
    private readonly ApplicationDbContext                _db;
    private readonly INotificationEmailService           _emailService;
    private readonly INotificationSmsService             _smsService;
    private readonly ClinicSettings                      _clinicSettings;
    private readonly ILogger<ReminderNotificationService> _logger;

    public ReminderNotificationService(
        ApplicationDbContext                  db,
        INotificationEmailService             emailService,
        INotificationSmsService               smsService,
        ClinicSettings                        clinicSettings,
        ILogger<ReminderNotificationService>  logger)
    {
        _db             = db;
        _emailService   = emailService;
        _smsService     = smsService;
        _clinicSettings = clinicSettings;
        _logger         = logger;
    }

    /// <inheritdoc/>
    public async Task<ReminderNotificationResult> SendReminderAsync(
        ReminderNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var notificationType = request.BatchType == ReminderBatchType.TwentyFourHour
                ? NotificationType.Reminder24h
                : NotificationType.Reminder2h;

            // ── 1. Re-check appointment eligibility at send time (AC-5) ──────
            // Appointments can be cancelled between batch selection and now; skip
            // and log CancelledBeforeSend without invoking any transport.
            var currentStatus = await GetAppointmentStatusAsync(request.AppointmentId, cancellationToken);

            if (currentStatus == AppointmentStatus.Cancelled)
            {
                _logger.LogInformation(
                    "[REMINDER-SKIP-CANCELLED] Appointment {AppointmentId} was cancelled " +
                    "before reminder send. BatchType={BatchType} Correlation={CorrelationId}",
                    request.AppointmentId, request.BatchType,
                    request.CorrelationId ?? "N/A");

                await PersistCancelledBeforeSendAsync(
                    request, notificationType, cancellationToken);

                return new ReminderNotificationResult(
                    EmailSent: false, SmsSent: false, SmsSkippedOptOut: false,
                    SmsSkippedChannel: false,
                    FailureReason: "cancelled-before-send");
            }

            // ── 2. Check per-channel deduplication (EC-1) ────────────────────
            var (emailAlreadySent, smsAlreadySent) = await CheckAlreadySentAsync(
                request.AppointmentId, notificationType, cancellationToken);

            // ── 3. Dispatch by batch type ─────────────────────────────────────
            return request.BatchType == ReminderBatchType.TwentyFourHour
                ? await SendTwentyFourHourReminderAsync(
                    request, notificationType, emailAlreadySent, smsAlreadySent, cancellationToken)
                : await SendTwoHourReminderAsync(
                    request, notificationType, smsAlreadySent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error in ReminderNotificationService for appointment {AppointmentId}. " +
                "BatchType={BatchType} Correlation={CorrelationId}",
                request.AppointmentId, request.BatchType, request.CorrelationId ?? "N/A");

            return new ReminderNotificationResult(
                EmailSent: false, SmsSent: false, SmsSkippedOptOut: false,
                FailureReason: $"Unexpected error: {ex.GetType().Name}");
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 24-hour reminder: email + SMS (AC-1, AC-3)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<ReminderNotificationResult> SendTwentyFourHourReminderAsync(
        ReminderNotificationRequest request,
        NotificationType            notificationType,
        bool                        emailAlreadySent,
        bool                        smsAlreadySent,
        CancellationToken           ct)
    {
        // ── Email (AC-1) ──────────────────────────────────────────────────────
        NotificationEmailResult? emailResult = null;
        if (!emailAlreadySent)
        {
            // Cancellation link: {portalBaseUrl}/appointments/{id}/cancel (EC-2)
            var cancellationLink =
                $"{_clinicSettings.PortalBaseUrl.TrimEnd('/')}/appointments/{request.AppointmentId}/cancel";

            var emailRequest = new NotificationEmailRequest(
                AppointmentId:    request.AppointmentId,
                PatientId:        request.PatientId,
                PatientEmail:     request.PatientEmail,
                PatientName:      request.PatientFullName,
                AppointmentTime:  request.AppointmentTimeUtc,
                ProviderName:     request.ProviderName,
                AppointmentType:  request.AppointmentType,
                NotificationType: notificationType,
                BookingReference: request.BookingReference,
                CancellationLink: cancellationLink,
                CorrelationId:    request.CorrelationId);

            emailResult = await _emailService.SendAsync(emailRequest, ct);
        }
        else
        {
            _logger.LogDebug(
                "[REMINDER-DEDUP] Email Reminder24h already sent for appointment {AppointmentId}. " +
                "Skipping. Correlation={CorrelationId}",
                request.AppointmentId, request.CorrelationId ?? "N/A");
        }

        // ── SMS (AC-1, AC-3) ──────────────────────────────────────────────────
        NotificationSmsResult? smsResult = null;
        bool smsSkippedOptOut = false;

        if (!smsAlreadySent && !string.IsNullOrWhiteSpace(request.PatientPhoneNumber))
        {
            var smsRequest = new NotificationSmsRequest(
                AppointmentId:      request.AppointmentId,
                PatientId:          request.PatientId,
                PatientPhoneNumber: request.PatientPhoneNumber,
                PatientName:        request.PatientFullName,
                AppointmentTime:    request.AppointmentTimeUtc,
                ProviderName:       request.ProviderName,
                AppointmentType:    request.AppointmentType,
                NotificationType:   notificationType,
                BookingReference:   request.BookingReference,
                CorrelationId:      request.CorrelationId);

            smsResult      = await _smsService.SendAsync(smsRequest, ct);
            smsSkippedOptOut = smsResult.IsOptedOut;
        }
        else if (smsAlreadySent)
        {
            _logger.LogDebug(
                "[REMINDER-DEDUP] SMS Reminder24h already sent for appointment {AppointmentId}. " +
                "Skipping. Correlation={CorrelationId}",
                request.AppointmentId, request.CorrelationId ?? "N/A");
        }

        var emailSent = emailAlreadySent || (emailResult?.Succeeded ?? false);
        var smsSent   = smsAlreadySent   || (smsResult?.Succeeded   ?? false);

        LogReminderOutcome(request, notificationType, emailResult, smsResult, emailAlreadySent, smsAlreadySent);

        return new ReminderNotificationResult(
            EmailSent:       emailSent,
            SmsSent:         smsSent,
            SmsSkippedOptOut: smsSkippedOptOut,
            FailureReason:   DeriveFailureReason(emailSent, smsSent, smsSkippedOptOut,
                                                  emailResult, smsResult));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // 2-hour reminder: SMS only (AC-2)
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<ReminderNotificationResult> SendTwoHourReminderAsync(
        ReminderNotificationRequest request,
        NotificationType            notificationType,
        bool                        smsAlreadySent,
        CancellationToken           ct)
    {
        NotificationSmsResult? smsResult = null;
        bool smsSkippedOptOut = false;

        if (!smsAlreadySent && !string.IsNullOrWhiteSpace(request.PatientPhoneNumber))
        {
            var smsRequest = new NotificationSmsRequest(
                AppointmentId:      request.AppointmentId,
                PatientId:          request.PatientId,
                PatientPhoneNumber: request.PatientPhoneNumber,
                PatientName:        request.PatientFullName,
                AppointmentTime:    request.AppointmentTimeUtc,
                ProviderName:       request.ProviderName,
                AppointmentType:    request.AppointmentType,
                NotificationType:   notificationType,
                BookingReference:   request.BookingReference,
                CorrelationId:      request.CorrelationId);

            smsResult        = await _smsService.SendAsync(smsRequest, ct);
            smsSkippedOptOut = smsResult.IsOptedOut;
        }
        else if (smsAlreadySent)
        {
            _logger.LogDebug(
                "[REMINDER-DEDUP] SMS Reminder2h already sent for appointment {AppointmentId}. " +
                "Skipping. Correlation={CorrelationId}",
                request.AppointmentId, request.CorrelationId ?? "N/A");
        }

        var smsSent = smsAlreadySent || (smsResult?.Succeeded ?? false);

        LogReminderOutcome(request, notificationType,
            emailResult: null, smsResult, emailAlreadySent: false, smsAlreadySent);

        // 2-hour batch never sends email — SmsSkippedChannel not set; email is
        // intentionally absent for this window (AC-2).
        return new ReminderNotificationResult(
            EmailSent:        false,
            SmsSent:          smsSent,
            SmsSkippedOptOut: smsSkippedOptOut,
            SmsSkippedChannel: false,
            FailureReason:    DeriveFailureReason(
                emailSent: false, smsSent, smsSkippedOptOut, emailResult: null, smsResult));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Re-reads the latest appointment status.  Returns <c>null</c> on failure so
    /// the caller defaults to proceeding (fail-open is safer than blocking all reminders
    /// on a transient DB read error).
    /// </summary>
    private async Task<AppointmentStatus?> GetAppointmentStatusAsync(
        Guid appointmentId, CancellationToken ct)
    {
        try
        {
            return await _db.Appointments
                .AsNoTracking()
                .Where(a => a.Id == appointmentId)
                .Select(a => (AppointmentStatus?)a.Status)
                .FirstOrDefaultAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not re-read appointment status for {AppointmentId}. " +
                "Proceeding as Scheduled to avoid blocking reminders on a transient error.",
                appointmentId);
            return null;
        }
    }

    /// <summary>
    /// Returns (emailAlreadySent, smsAlreadySent) for the given appointment and
    /// notification type by querying <c>NotificationLog</c>.  Used to prevent
    /// double-sends when a checkpointed batch resumes (EC-1).
    /// </summary>
    private async Task<(bool emailSent, bool smsSent)> CheckAlreadySentAsync(
        Guid appointmentId, NotificationType notificationType, CancellationToken ct)
    {
        try
        {
            var sent = await _db.NotificationLogs
                .AsNoTracking()
                .Where(n =>
                    n.AppointmentId    == appointmentId &&
                    n.NotificationType == notificationType &&
                    n.Status           == NotificationStatus.Sent)
                .Select(n => n.DeliveryChannel)
                .ToListAsync(ct);

            return (
                emailSent: sent.Contains(DeliveryChannel.Email),
                smsSent:   sent.Contains(DeliveryChannel.Sms));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not check dedup NotificationLog for appointment {AppointmentId}. " +
                "Proceeding without deduplication — a duplicate send may occur on resume.",
                appointmentId);
            return (false, false);
        }
    }

    /// <summary>
    /// Writes <see cref="NotificationStatus.CancelledBeforeSend"/> log rows for all
    /// channels relevant to the batch type so the skip is auditable (AC-5).
    /// </summary>
    private async Task PersistCancelledBeforeSendAsync(
        ReminderNotificationRequest request,
        NotificationType            notificationType,
        CancellationToken           ct)
    {
        var channels = request.BatchType == ReminderBatchType.TwentyFourHour
            ? new[] { DeliveryChannel.Email, DeliveryChannel.Sms }
            : new[] { DeliveryChannel.Sms };

        try
        {
            foreach (var channel in channels)
            {
                _db.NotificationLogs.Add(new NotificationLog
                {
                    NotificationId   = Guid.NewGuid(),
                    AppointmentId    = request.AppointmentId,
                    NotificationType = notificationType,
                    DeliveryChannel  = channel,
                    Status           = NotificationStatus.CancelledBeforeSend,
                    RetryCount       = 0,
                    SentAt           = null,
                    CreatedAt        = DateTime.UtcNow,
                });
            }

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Log-only: persistence failure must not change the caller's skip decision.
            _logger.LogError(ex,
                "Failed to persist CancelledBeforeSend log for appointment {AppointmentId}.",
                request.AppointmentId);
        }
    }

    /// <summary>
    /// Returns a non-null failure reason when neither channel produced a countable outcome,
    /// so the batch scheduler can log and count it as a failed record.
    /// </summary>
    private static string? DeriveFailureReason(
        bool                     emailSent,
        bool                     smsSent,
        bool                     smsSkippedOptOut,
        NotificationEmailResult? emailResult,
        NotificationSmsResult?   smsResult)
    {
        if (emailSent || smsSent || smsSkippedOptOut)
            return null;

        if (emailResult is { IsBounced: true })
            return "email-bounced";

        if (smsResult is { IsGatewayDisabled: true })
            return "sms-gateway-disabled";

        if (smsResult is { IsInvalidNumber: true })
            return "sms-invalid-number";

        return "all-channels-failed";
    }

    private void LogReminderOutcome(
        ReminderNotificationRequest request,
        NotificationType            notificationType,
        NotificationEmailResult?    emailResult,
        NotificationSmsResult?      smsResult,
        bool                        emailAlreadySent,
        bool                        smsAlreadySent)
    {
        var emailStatus = emailAlreadySent
            ? "dedup-skipped"
            : emailResult is null
                ? "not-sent"
                : emailResult.Succeeded ? "sent" : (emailResult.IsBounced ? "bounced" : "failed");

        var smsStatus = smsAlreadySent
            ? "dedup-skipped"
            : smsResult is null
                ? "not-sent"
                : smsResult.IsOptedOut
                    ? "opted-out"
                    : smsResult.IsGatewayDisabled
                        ? "gateway-disabled"
                        : smsResult.Succeeded ? "sent" : "failed";

        _logger.LogInformation(
            "[REMINDER-OUTCOME] {NotificationType} appointment={AppointmentId} " +
            "email={EmailStatus} sms={SmsStatus} Correlation={CorrelationId}",
            notificationType, request.AppointmentId,
            emailStatus, smsStatus, request.CorrelationId ?? "N/A");
    }
}
