using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Appointments;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Orchestrates appointment-event email delivery.
///
/// Responsibilities:
/// <list type="bullet">
///   <item>Render the appropriate template via <see cref="EmailTemplateRenderer"/> (AC-4).</item>
///   <item>Invoke <see cref="IEmailTransport"/> to send the composed message (AC-1, AC-3).</item>
///   <item>
///     Persist a <c>NotificationLog</c> row with the final outcome status (Sent / Failed /
///     Bounced), retry count, delivery timestamp, and the provider used after any
///     failover (AC-2, EC-1).
///   </item>
///   <item>
///     When a bounce is detected, flag the patient's contact details for staff follow-up
///     by setting <c>Patient.ContactUpdateRequired = true</c> (EC-2).
///   </item>
///   <item>
///     When the fallback provider was engaged, emit a <c>LogCritical</c> operational
///     alert so the SendGrid quota situation is visible to administrators (EC-1).
///   </item>
/// </list>
///
/// This service never throws; all outcomes are encoded in <see cref="NotificationEmailResult"/>.
/// </summary>
public sealed class NotificationEmailService : INotificationEmailService
{
    private readonly ApplicationDbContext                         _db;
    private readonly IEmailTransport                             _transport;
    private readonly EmailTemplateRenderer                       _renderer;
    private readonly ClinicSettings                              _clinicSettings;
    private readonly INotificationDeliveryReliabilityService     _reliabilityService;
    private readonly ILogger<NotificationEmailService>           _logger;

    public NotificationEmailService(
        ApplicationDbContext                          db,
        IEmailTransport                              transport,
        EmailTemplateRenderer                        renderer,
        ClinicSettings                               clinicSettings,
        INotificationDeliveryReliabilityService      reliabilityService,
        ILogger<NotificationEmailService>            logger)
    {
        _db                 = db;
        _transport          = transport;
        _renderer           = renderer;
        _clinicSettings     = clinicSettings;
        _reliabilityService = reliabilityService;
        _logger             = logger;
    }

    /// <inheritdoc/>
    public async Task<NotificationEmailResult> SendAsync(
        NotificationEmailRequest request,
        CancellationToken cancellationToken = default)
    {
        // ── 1. Render template (AC-4) ──────────────────────────────────────────
        RenderedEmail rendered;
        try
        {
            rendered = _renderer.Render(request, _clinicSettings.TimeZoneId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Template rendering failed for {NotificationType} / appointment {AppointmentId}. " +
                "No email will be sent.",
                request.NotificationType, request.AppointmentId);

            await PersistLogAsync(
                request,
                providerName: "N/A",
                status:       NotificationStatus.Failed,
                retryCount:   0,
                sentAt:       null,
                cancellationToken);

            return new NotificationEmailResult(
                Succeeded: false, IsBounced: false,
                UsedFallbackProvider: false,
                ProviderName: "N/A", AttemptsMade: 0);
        }

        // ── 2. Build transport message ─────────────────────────────────────────
        var transportMsg = new EmailTransportMessage(
            ToAddress:     request.PatientEmail,
            ToName:        request.PatientName,
            Subject:       rendered.Subject,
            HtmlBody:      rendered.HtmlBody,
            PlainTextBody: rendered.PlainTextBody);

        // ── 3. Send via SMTP transport (retries + failover inside transport) ───
        var deliveryResult = await _transport.SendAsync(transportMsg, cancellationToken);

        // ── 5. Operational alert when fallback provider was used (EC-1) ──────
        if (deliveryResult.UsedFallback)
        {
            _logger.LogCritical(
                "[ADMIN ALERT] EP-005 email for appointment {AppointmentId} (type: {Type}) " +
                "was delivered via fallback provider '{Provider}'. " +
                "The primary SMTP provider quota or availability has been exhausted. " +
                "Correlation: {CorrelationId}",
                request.AppointmentId,
                request.NotificationType,
                deliveryResult.ProviderName,
                request.CorrelationId ?? "N/A");
        }

        // ── 6. Build result ────────────────────────────────────────────────────
        var serviceResult = new NotificationEmailResult(
            Succeeded:            deliveryResult.IsSuccess,
            IsBounced:            deliveryResult.IsBounced,
            UsedFallbackProvider: deliveryResult.UsedFallback,
            ProviderName:         deliveryResult.ProviderName,
            AttemptsMade:         deliveryResult.AttemptsMade);

        // ── 7. Delegate outcome to reliability orchestration (US_037 AC-1/AC-2/AC-3/EC-2) ──
        // Reliability service writes the NotificationLog via BufferedNotificationLogWriter,
        // flags the patient for contact validation on bounce, and schedules retries or marks
        // the notification permanently_failed when all retries are exhausted.
        await _reliabilityService.HandleEmailOutcomeAsync(request, serviceResult, cancellationToken);

        // ── 8. Structured diagnostics ─────────────────────────────────────────
        if (deliveryResult.IsSuccess)
        {
            _logger.LogInformation(
                "Notification email {Type} for appointment {AppointmentId} sent via '{Provider}' " +
                "in {Attempts} attempt(s). Correlation: {CorrelationId}",
                request.NotificationType, request.AppointmentId,
                deliveryResult.ProviderName, deliveryResult.AttemptsMade,
                request.CorrelationId ?? "N/A");
        }
        else if (deliveryResult.IsBounced)
        {
            _logger.LogWarning(
                "Notification email {Type} for appointment {AppointmentId} bounced via '{Provider}'. " +
                "Patient {PatientId} flagged for contact update. Correlation: {CorrelationId}",
                request.NotificationType, request.AppointmentId,
                deliveryResult.ProviderName, request.PatientId,
                request.CorrelationId ?? "N/A");
        }
        else
        {
            _logger.LogError(
                "Notification email {Type} for appointment {AppointmentId} failed via '{Provider}' " +
                "after {Attempts} attempt(s): {Reason}. Correlation: {CorrelationId}",
                request.NotificationType, request.AppointmentId,
                deliveryResult.ProviderName, deliveryResult.AttemptsMade,
                deliveryResult.FailureReason, request.CorrelationId ?? "N/A");
        }

        return serviceResult;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Persists a <see cref="NotificationLog"/> row for non-retryable early exits
    /// (e.g. template rendering failure) where the reliability orchestration path
    /// is not engaged.
    /// </summary>
    private async Task PersistLogAsync(
        NotificationEmailRequest request,
        string providerName,
        NotificationStatus status,
        int retryCount,
        DateTime? sentAt,
        CancellationToken ct)
    {
        try
        {
            _db.NotificationLogs.Add(new NotificationLog
            {
                NotificationId   = Guid.NewGuid(),
                AppointmentId    = request.AppointmentId,
                NotificationType = request.NotificationType,
                DeliveryChannel  = DeliveryChannel.Email,
                Status           = status,
                RetryCount       = retryCount,
                SentAt           = sentAt,
                CreatedAt        = DateTime.UtcNow,
            });

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to persist NotificationLog for appointment {AppointmentId} " +
                "(provider: {Provider}, status: {Status}).",
                request.AppointmentId, providerName, status);
        }
    }
}
