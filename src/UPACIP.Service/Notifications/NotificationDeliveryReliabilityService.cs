using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Scoped implementation of <see cref="INotificationDeliveryReliabilityService"/>.
///
/// Uses <see cref="BufferedNotificationLogWriter"/> for all attempt log writes so that
/// a transient persistence failure never loses the delivery outcome record.
/// Uses <see cref="INotificationRetryQueue"/> (the singleton <see cref="NotificationRetryWorker"/>)
/// to schedule backoff retries without creating a circular service dependency.
/// </summary>
public sealed class NotificationDeliveryReliabilityService : INotificationDeliveryReliabilityService
{
    // Backoff intervals indexed by AttemptNumber (1=1min, 2=5min, 3=15min).
    private static readonly TimeSpan[] BackoffSchedule =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
    ];

    private const int MaxOrchestrationAttempts = 3;

    private readonly ApplicationDbContext                        _db;
    private readonly BufferedNotificationLogWriter               _buffer;
    private readonly INotificationRetryQueue                     _retryQueue;
    private readonly ILogger<NotificationDeliveryReliabilityService> _logger;

    public NotificationDeliveryReliabilityService(
        ApplicationDbContext                             db,
        BufferedNotificationLogWriter                    buffer,
        INotificationRetryQueue                          retryQueue,
        ILogger<NotificationDeliveryReliabilityService>  logger)
    {
        _db         = db;
        _buffer     = buffer;
        _retryQueue = retryQueue;
        _logger     = logger;
    }

    /// <inheritdoc/>
    public async Task HandleEmailOutcomeAsync(
        NotificationEmailRequest request,
        NotificationEmailResult  result,
        CancellationToken        ct = default)
    {
        var now    = DateTime.UtcNow;
        var status = DeriveEmailStatus(result, request.OrchestrationAttemptNumber);

        await _buffer.WriteAsync(new NotificationLog
        {
            NotificationId   = Guid.NewGuid(),
            AppointmentId    = request.AppointmentId,
            NotificationType = request.NotificationType,
            DeliveryChannel  = DeliveryChannel.Email,
            Status           = status,
            RetryCount       = request.OrchestrationAttemptNumber,
            SentAt           = result.Succeeded ? now : null,
            CreatedAt        = now,
        }, ct);

        if (result.IsBounced)
        {
            // Bounced email is a contact-quality problem — do not retry (EC-2).
            await FlagPatientContactAsync(request.PatientId, request.AppointmentId, ct);
            return;
        }

        if (!result.Succeeded)
        {
            HandleFailedOutcome(
                request.AppointmentId,
                request.PatientId,
                request.PatientEmail,
                string.Empty,
                request.PatientName,
                DeliveryChannel.Email,
                request.NotificationType,
                request.AppointmentTime,
                request.ProviderName,
                request.AppointmentType,
                request.BookingReference,
                request.CancellationLink,
                request.OrchestrationAttemptNumber,
                request.CorrelationId);
        }
    }

    /// <inheritdoc/>
    public async Task HandleSmsOutcomeAsync(
        NotificationSmsRequest request,
        NotificationSmsResult  result,
        CancellationToken      ct = default)
    {
        var now    = DateTime.UtcNow;
        var status = DeriveSmsStatus(result, request.OrchestrationAttemptNumber);

        await _buffer.WriteAsync(new NotificationLog
        {
            NotificationId   = Guid.NewGuid(),
            AppointmentId    = request.AppointmentId,
            NotificationType = request.NotificationType,
            DeliveryChannel  = DeliveryChannel.Sms,
            Status           = status,
            RetryCount       = request.OrchestrationAttemptNumber,
            SentAt           = result.Succeeded ? now : null,
            CreatedAt        = now,
        }, ct);

        // Gateway disabled or invalid number: non-retryable channel-level outcomes.
        if (!result.Succeeded && !result.IsGatewayDisabled && !result.IsInvalidNumber && !result.IsOptedOut)
        {
            HandleFailedOutcome(
                request.AppointmentId,
                request.PatientId,
                string.Empty,
                request.PatientPhoneNumber,
                request.PatientName,
                DeliveryChannel.Sms,
                request.NotificationType,
                request.AppointmentTime,
                request.ProviderName,
                request.AppointmentType,
                request.BookingReference,
                null,
                request.OrchestrationAttemptNumber,
                request.CorrelationId);
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void HandleFailedOutcome(
        Guid            appointmentId,
        Guid            patientId,
        string          patientEmail,
        string          patientPhoneNumber,
        string          patientName,
        DeliveryChannel channel,
        NotificationType notificationType,
        DateTime        appointmentTime,
        string?         providerName,
        string?         appointmentType,
        string?         bookingReference,
        string?         cancellationLink,
        int             orchestrationAttemptNumber,
        string?         correlationId)
    {
        if (orchestrationAttemptNumber >= MaxOrchestrationAttempts)
        {
            // All retries exhausted — already logged as PermanentlyFailed above.
            _logger.LogWarning(
                "[STAFF REVIEW] Notification {Channel} {Type} for appointment {AppointmentId} " +
                "is permanently failed after {Attempts} orchestration attempt(s). " +
                "Manual follow-up required. Patient: {PatientId}. Correlation: {CorrelationId}",
                channel, notificationType, appointmentId,
                orchestrationAttemptNumber + 1,
                patientId, correlationId ?? "N/A");
            return;
        }

        // Schedule next retry with backoff.
        var nextAttempt  = orchestrationAttemptNumber + 1;
        var backoff      = BackoffSchedule[orchestrationAttemptNumber]; // index 0=after attempt 0
        var nextRetryAt  = DateTime.UtcNow.Add(backoff);

        _retryQueue.EnqueueRetry(new NotificationRetryRequest(
            AppointmentId:    appointmentId,
            PatientId:        patientId,
            PatientEmail:     patientEmail,
            PatientPhoneNumber: patientPhoneNumber,
            PatientName:      patientName,
            Channel:          channel,
            NotificationType: notificationType,
            AppointmentTime:  appointmentTime,
            ProviderName:     providerName,
            AppointmentType:  appointmentType,
            BookingReference: bookingReference,
            CancellationLink: cancellationLink,
            AttemptNumber:    nextAttempt,
            NextRetryAt:      nextRetryAt,
            CorrelationId:    correlationId));

        _logger.LogInformation(
            "Notification {Channel} {Type} for appointment {AppointmentId} scheduled for retry " +
            "{AttemptNumber}/{MaxAttempts} at {NextRetryAt:u}. Correlation: {CorrelationId}",
            channel, notificationType, appointmentId,
            nextAttempt, MaxOrchestrationAttempts, nextRetryAt,
            correlationId ?? "N/A");
    }

    private async Task FlagPatientContactAsync(Guid patientId, Guid appointmentId, CancellationToken ct)
    {
        try
        {
            var affected = await _db.Patients
                .Where(p => p.Id == patientId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(p => p.ContactUpdateRequired, true)
                        .SetProperty(p => p.ContactUpdateRequestedAt, DateTime.UtcNow),
                    ct);

            if (affected == 0)
            {
                _logger.LogWarning(
                    "Cannot flag contact update: patient {PatientId} not found " +
                    "(appointment {AppointmentId}).",
                    patientId, appointmentId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to flag contact update for patient {PatientId} " +
                "(appointment {AppointmentId}).",
                patientId, appointmentId);
        }
    }

    private static NotificationStatus DeriveEmailStatus(
        NotificationEmailResult result,
        int                     orchestrationAttempt)
    {
        if (result.Succeeded) return NotificationStatus.Sent;
        if (result.IsBounced) return NotificationStatus.Bounced;
        if (orchestrationAttempt >= MaxOrchestrationAttempts) return NotificationStatus.PermanentlyFailed;
        return NotificationStatus.Failed;
    }

    private static NotificationStatus DeriveSmsStatus(
        NotificationSmsResult result,
        int                   orchestrationAttempt)
    {
        if (result.Succeeded)          return NotificationStatus.Sent;
        if (result.IsOptedOut)         return NotificationStatus.OptedOut;
        if (orchestrationAttempt >= MaxOrchestrationAttempts) return NotificationStatus.PermanentlyFailed;
        return NotificationStatus.Failed;
    }
}
