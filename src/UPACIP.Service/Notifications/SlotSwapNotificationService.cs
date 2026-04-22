using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Auth;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Production implementation of <see cref="ISlotSwapNotificationService"/> (US_036 AC-2).
///
/// Sends slot-swap completion notifications via email and SMS.  The email is dispatched via
/// <see cref="IEmailService.SendSwapCompletedEmailAsync"/> — the pre-existing template that
/// accepts formatted old/new time strings.  The SMS is sent directly via
/// <see cref="ISmsTransport"/> after consulting the patient's opt-out preference.
///
/// EC-1 (invalid-contact): delivery failures are logged but never trigger a swap rollback.
///   The completed swap is the authoritative source of truth; notification failure is a
///   delivery concern only.
///
/// This service never throws.
/// </summary>
public sealed class SlotSwapNotificationService : ISlotSwapNotificationService
{
    private readonly IEmailService                          _emailService;
    private readonly ISmsTransport                          _smsTransport;
    private readonly ApplicationDbContext                   _db;
    private readonly ILogger<SlotSwapNotificationService>   _logger;

    public SlotSwapNotificationService(
        IEmailService                        emailService,
        ISmsTransport                        smsTransport,
        ApplicationDbContext                 db,
        ILogger<SlotSwapNotificationService> logger)
    {
        _emailService = emailService;
        _smsTransport = smsTransport;
        _db           = db;
        _logger       = logger;
    }

    /// <inheritdoc/>
    public async Task<SlotSwapNotificationResult> SendSwapNotificationAsync(
        SlotSwapNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        var emailSent        = false;
        var emailFailed      = false;
        var smsSent          = false;
        var smsSkippedOptOut = false;

        // ── 1. Send swap-completed email ─────────────────────────────────────
        //    EC-1: failure is logged without rolling back the swap.
        try
        {
            await _emailService.SendSwapCompletedEmailAsync(
                request.PatientEmail,
                request.PatientFullName,
                request.OldTimeFormatted,
                request.NewTimeFormatted,
                request.ProviderName ?? string.Empty,
                cancellationToken);

            emailSent = true;

            // Persist email outcome to NotificationLog (AC-2 audit)
            await PersistNotificationLogAsync(
                request.AppointmentId,
                NotificationType.SlotSwapCompleted,
                DeliveryChannel.Email,
                NotificationStatus.Sent,
                cancellationToken);

            _logger.LogInformation(
                "[SWAP-NOTIFY-EMAIL] Sent for appointment {AppointmentId}. " +
                "Correlation: {CorrelationId}",
                request.AppointmentId, request.CorrelationId ?? "N/A");
        }
        catch (Exception ex)
        {
            emailFailed = true;

            // Persist failure outcome (EC-1: log without rollback)
            await PersistNotificationLogAsync(
                request.AppointmentId,
                NotificationType.SlotSwapCompleted,
                DeliveryChannel.Email,
                NotificationStatus.Failed,
                cancellationToken);

            _logger.LogError(ex,
                "[SWAP-NOTIFY-EMAIL] Delivery failed for appointment {AppointmentId}. " +
                "Swap remains committed. Correlation: {CorrelationId}",
                request.AppointmentId, request.CorrelationId ?? "N/A");
        }

        // ── 2. Send swap-completed SMS ───────────────────────────────────────
        if (string.IsNullOrWhiteSpace(request.PatientPhoneNumber))
        {
            _logger.LogDebug(
                "[SWAP-NOTIFY-SMS] No phone number for appointment {AppointmentId} — SMS skipped.",
                request.AppointmentId);
        }
        else
        {
            // 2a. Consult patient opt-out preference
            bool optedOut;
            try
            {
                optedOut = await _db.Patients
                    .Where(p => p.Id == request.PatientId)
                    .Select(p => p.SmsOptedOut)
                    .FirstOrDefaultAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                // Fail closed — cannot confirm opt-in; skip SMS as safe default (EC-1)
                _logger.LogError(ex,
                    "[SWAP-NOTIFY-SMS] Failed to read SMS opt-out for patient {PatientId} " +
                    "(appointment {AppointmentId}). Skipping SMS. Correlation: {CorrelationId}",
                    request.PatientId, request.AppointmentId, request.CorrelationId ?? "N/A");

                goto SmsDone;
            }

            if (optedOut)
            {
                smsSkippedOptOut = true;

                await PersistNotificationLogAsync(
                    request.AppointmentId,
                    NotificationType.SlotSwapCompleted,
                    DeliveryChannel.Sms,
                    NotificationStatus.OptedOut,
                    cancellationToken);

                _logger.LogInformation(
                    "[SWAP-NOTIFY-SMS] Skipped for appointment {AppointmentId} — patient opted out. " +
                    "Correlation: {CorrelationId}",
                    request.AppointmentId, request.CorrelationId ?? "N/A");
            }
            else
            {
                // 2b. Compose and send SMS
                try
                {
                    var body    = BuildSmsBody(request);
                    var message = new SmsTransportMessage(request.PatientPhoneNumber, body);
                    var result  = await _smsTransport.SendAsync(message, cancellationToken);

                    smsSent = result.IsSuccess;

                    var smsStatus = result.Outcome switch
                    {
                        SmsDeliveryOutcome.Sent           => NotificationStatus.Sent,
                        SmsDeliveryOutcome.InvalidNumber  => NotificationStatus.Failed,
                        SmsDeliveryOutcome.GatewayDisabled => NotificationStatus.Failed,
                        _                                  => NotificationStatus.Failed,
                    };

                    await PersistNotificationLogAsync(
                        request.AppointmentId,
                        NotificationType.SlotSwapCompleted,
                        DeliveryChannel.Sms,
                        smsStatus,
                        cancellationToken);

                    _logger.LogInformation(
                        "[SWAP-NOTIFY-SMS] Outcome={Outcome} for appointment {AppointmentId}. " +
                        "Correlation: {CorrelationId}",
                        result.Outcome, request.AppointmentId, request.CorrelationId ?? "N/A");
                }
                catch (Exception ex)
                {
                    // EC-1: log without rollback
                    await PersistNotificationLogAsync(
                        request.AppointmentId,
                        NotificationType.SlotSwapCompleted,
                        DeliveryChannel.Sms,
                        NotificationStatus.Failed,
                        cancellationToken);

                    _logger.LogError(ex,
                        "[SWAP-NOTIFY-SMS] Transport error for appointment {AppointmentId}. " +
                        "Swap remains committed. Correlation: {CorrelationId}",
                        request.AppointmentId, request.CorrelationId ?? "N/A");
                }
            }
        }

        SmsDone:
        return new SlotSwapNotificationResult(
            EmailSent:        emailSent,
            SmsSent:          smsSent,
            SmsSkippedOptOut: smsSkippedOptOut,
            EmailFailed:      emailFailed);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static string BuildSmsBody(SlotSwapNotificationRequest request)
    {
        var provider = string.IsNullOrWhiteSpace(request.ProviderName)
            ? string.Empty
            : $" with {request.ProviderName}";

        return $"Hi {request.PatientFullName}, your appointment has been moved. " +
               $"Old: {request.OldTimeFormatted}. " +
               $"New: {request.NewTimeFormatted}{provider}. " +
               $"No action needed — your booking is confirmed.";
    }

    private async Task PersistNotificationLogAsync(
        Guid                appointmentId,
        NotificationType    notificationType,
        DeliveryChannel     channel,
        NotificationStatus  status,
        CancellationToken   cancellationToken)
    {
        try
        {
            _db.NotificationLogs.Add(new NotificationLog
            {
                NotificationId   = Guid.NewGuid(),
                AppointmentId    = appointmentId,
                NotificationType = notificationType,
                DeliveryChannel  = channel,
                Status           = status,
                RetryCount       = 0,
                SentAt           = status == NotificationStatus.Sent ? DateTime.UtcNow : null,
                CreatedAt        = DateTime.UtcNow,
            });

            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            // Log-only; never let a log-persistence failure surface to the caller
            _logger.LogError(ex,
                "Failed to persist NotificationLog for appointment {AppointmentId} " +
                "channel={Channel} status={Status}.",
                appointmentId, channel, status);
        }
    }
}
