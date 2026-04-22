using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Coordinates immediate booking-confirmation delivery after a successful
/// appointment commit (US_034).
///
/// Workflow:
/// <list type="number">
///   <item>Re-read latest appointment status from the DB so a near-simultaneous
///     cancellation is reflected in the confirmation content rather than stale
///     "scheduled" language (EC-2).</item>
///   <item>Attempt PDF generation.  If the service returns failure or is not yet
///     configured, proceed without attachment and log a retry-needed flag (EC-1).</item>
///   <item>Send confirmation email with a prefilled cancellation link and the PDF
///     attachment (if generated) via <see cref="INotificationEmailService"/> (AC-1, AC-3).</item>
///   <item>Send confirmation SMS when the patient has not opted out (AC-1, AC-4).</item>
///   <item>Skip SMS for opted-out patients and persist <c>OptedOut</c> (AC-4).</item>
///   <item>Emit an operational alert and continue in email-only mode when the SMS
///     gateway is disabled or Twilio credits are exhausted (EC-1).</item>
/// </list>
///
/// This service never throws; all outcomes are encoded in
/// <see cref="BookingConfirmationNotificationResult"/>.
/// </summary>
public sealed class BookingConfirmationNotificationService : IBookingConfirmationNotificationService
{
    private readonly ApplicationDbContext                          _db;
    private readonly INotificationEmailService                     _emailService;
    private readonly INotificationSmsService                       _smsService;
    private readonly IPdfConfirmationService                       _pdfService;
    private readonly ILogger<BookingConfirmationNotificationService> _logger;

    public BookingConfirmationNotificationService(
        ApplicationDbContext                            db,
        INotificationEmailService                       emailService,
        INotificationSmsService                         smsService,
        IPdfConfirmationService                         pdfService,
        ILogger<BookingConfirmationNotificationService> logger)
    {
        _db           = db;
        _emailService  = emailService;
        _smsService    = smsService;
        _pdfService    = pdfService;
        _logger        = logger;
    }

    /// <inheritdoc/>
    public async Task<BookingConfirmationNotificationResult> SendAsync(
        BookingConfirmationRequest request,
        CancellationToken cancellationToken = default)
    {
        // ── 1. Re-read latest appointment status (EC-2: cancellation race) ───
        // If the appointment was cancelled between the booking commit and now, the
        // confirmation content must reflect that instead of stale "scheduled" language.
        bool isAlreadyCancelled = false;
        try
        {
            var status = await _db.Appointments
                .AsNoTracking()
                .Where(a => a.Id == request.AppointmentId)
                .Select(a => a.Status)
                .FirstOrDefaultAsync(cancellationToken);

            isAlreadyCancelled = status == AppointmentStatus.Cancelled;
        }
        catch (Exception ex)
        {
            // Non-fatal — treat as not cancelled and proceed.  The appointment
            // was committed successfully; a status re-read failure should not
            // block the 30-second confirmation window (AC-1).
            _logger.LogWarning(ex,
                "Could not re-read appointment status for {AppointmentId} before " +
                "composing confirmation. Proceeding as Scheduled. Correlation: {CorrelationId}",
                request.AppointmentId, request.CorrelationId ?? "N/A");
        }

        if (isAlreadyCancelled)
        {
            _logger.LogWarning(
                "Confirmation for appointment {AppointmentId} is proceeding with " +
                "a 'cancelled' note — cancellation was processed first (EC-2). " +
                "Correlation: {CorrelationId}",
                request.AppointmentId, request.CorrelationId ?? "N/A");
        }

        // ── 2. Attempt PDF generation (EC-1) ─────────────────────────────────
        var pdfContext = new PdfConfirmationContext(
            AppointmentId:    request.AppointmentId,
            PatientName:      request.PatientName,
            AppointmentTime:  request.AppointmentTime,
            ProviderName:     request.ProviderName,
            AppointmentType:  request.AppointmentType,
            BookingReference: request.BookingReference,
            IsAlreadyCancelled: isAlreadyCancelled);

        var pdfResult = await SafeGeneratePdfAsync(pdfContext, request, cancellationToken);

        // ── 3. Send confirmation email (AC-1, AC-3) ──────────────────────────
        // Prefilled cancellation link format: {baseUrl}/appointments/{id}/cancel
        // OWASP A03: CancellationBaseUrl comes from server config (not user input)
        // so no HTML-encoding needed here; the template encoder handles it in rendering.
        var cancellationLink = isAlreadyCancelled
            ? null
            : $"{request.CancellationBaseUrl.TrimEnd('/')}/appointments/{request.AppointmentId}/cancel";

        var emailRequest = new NotificationEmailRequest(
            AppointmentId:      request.AppointmentId,
            PatientId:          request.PatientId,
            PatientEmail:       request.PatientEmail,
            PatientName:        request.PatientName,
            AppointmentTime:    request.AppointmentTime,
            ProviderName:       request.ProviderName,
            AppointmentType:    request.AppointmentType,
            NotificationType:   NotificationType.Confirmation,
            BookingReference:   request.BookingReference,
            CancellationLink:   cancellationLink,
            IsAlreadyCancelled: isAlreadyCancelled,
            CorrelationId:      request.CorrelationId);

        var emailResult = await _emailService.SendAsync(emailRequest, cancellationToken);

        // ── 4. Send confirmation SMS (AC-1, AC-4) ────────────────────────────
        // Skip SMS silently when the phone number is absent (walk-in / legacy data).
        NotificationSmsResult? smsResult = null;
        if (!string.IsNullOrWhiteSpace(request.PatientPhoneNumber))
        {
            var smsRequest = new NotificationSmsRequest(
                AppointmentId:    request.AppointmentId,
                PatientId:        request.PatientId,
                PatientPhoneNumber: request.PatientPhoneNumber,
                PatientName:      request.PatientName,
                AppointmentTime:  request.AppointmentTime,
                ProviderName:     request.ProviderName,
                AppointmentType:  request.AppointmentType,
                NotificationType: NotificationType.Confirmation,
                BookingReference: request.BookingReference,
                CorrelationId:    request.CorrelationId);

            smsResult = await _smsService.SendAsync(smsRequest, cancellationToken);
        }

        // ── 5. Structured diagnostics ─────────────────────────────────────────
        LogOutcome(request, emailResult, smsResult, pdfResult, isAlreadyCancelled);

        return new BookingConfirmationNotificationResult(
            EmailSucceeded:     emailResult.Succeeded,
            SmsSucceeded:       smsResult?.Succeeded ?? false,
            SmsOptedOut:        smsResult?.IsOptedOut ?? false,
            SmsGatewayDisabled: smsResult?.IsGatewayDisabled ?? false,
            PdfAttached:        pdfResult.IsSuccess,
            PdfRetryNeeded:     !pdfResult.IsSuccess && !pdfResult.IsServiceUnavailable,
            IsAlreadyCancelled: isAlreadyCancelled);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls the PDF service with a try/catch so a generation failure never
    /// blocks the confirmation email (EC-1).
    /// </summary>
    private async Task<PdfConfirmationResult> SafeGeneratePdfAsync(
        PdfConfirmationContext pdfContext,
        BookingConfirmationRequest request,
        CancellationToken ct)
    {
        try
        {
            var result = await _pdfService.GenerateAsync(pdfContext, ct);

            if (!result.IsSuccess && !result.IsServiceUnavailable)
            {
                // Real failure (not just "service not configured") — log retry needed.
                _logger.LogWarning(
                    "[PDF-RETRY-NEEDED] PDF confirmation generation failed for " +
                    "appointment {AppointmentId} (ref: {BookingReference}). " +
                    "Email will be sent without attachment. Reason: {Reason}. " +
                    "Correlation: {CorrelationId}",
                    request.AppointmentId, request.BookingReference,
                    result.FailureReason, request.CorrelationId ?? "N/A");
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[PDF-RETRY-NEEDED] PDF service threw unexpectedly for " +
                "appointment {AppointmentId}. Email will be sent without attachment. " +
                "Correlation: {CorrelationId}",
                request.AppointmentId, request.CorrelationId ?? "N/A");

            return PdfConfirmationResult.Failed("Unexpected exception in PDF generation.");
        }
    }

    private void LogOutcome(
        BookingConfirmationRequest request,
        NotificationEmailResult    emailResult,
        NotificationSmsResult?     smsResult,
        PdfConfirmationResult      pdfResult,
        bool                       isAlreadyCancelled)
    {
        _logger.LogInformation(
            "BookingConfirmation for appointment {AppointmentId} (ref: {BookingReference}): " +
            "email={EmailStatus}, sms={SmsStatus}, pdf={PdfStatus}, cancelled={Cancelled}. " +
            "Correlation: {CorrelationId}",
            request.AppointmentId, request.BookingReference,
            emailResult.Succeeded ? "sent" : "failed",
            smsResult is null
                ? "no-phone"
                : smsResult.IsOptedOut
                    ? "opted-out"
                    : smsResult.IsGatewayDisabled
                        ? "gateway-disabled"
                        : smsResult.Succeeded ? "sent" : "failed",
            pdfResult.IsSuccess
                ? "attached"
                : pdfResult.IsServiceUnavailable ? "service-not-configured" : "retry-needed",
            isAlreadyCancelled,
            request.CorrelationId ?? "N/A");
    }
}
