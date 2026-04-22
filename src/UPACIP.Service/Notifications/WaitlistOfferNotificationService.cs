using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.Service.Auth;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Production implementation of <see cref="IWaitlistOfferNotificationService"/> (US_036 AC-1).
///
/// Sends waitlist offer messages via:
/// <list type="bullet">
///   <item><b>Email:</b> <see cref="IEmailService.SendWaitlistOfferEmailAsync"/> — the pre-existing
///     transport that does not require an <c>AppointmentId</c> FK (no appointment exists at offer
///     time; using <see cref="INotificationEmailService"/> would violate the FK constraint on
///     <c>notification_logs.appointment_id</c>).</item>
///   <item><b>SMS:</b> <see cref="ISmsTransport"/> invoked directly after consulting the
///     patient's <c>SmsOptedOut</c> preference in the database.  This mirrors the opt-out
///     check pattern in <see cref="NotificationSmsService"/> without requiring a
///     <c>notification_logs</c> row (AC-3, EC-2).</item>
/// </list>
///
/// Invalid-contact detection (EC-1):
/// When the email is permanently bounced AND the phone number is absent or rejected as an
/// invalid number, <see cref="WaitlistOfferNotificationResult.IsInvalidContact"/> is set so
/// <see cref="UPACIP.Service.Appointments.WaitlistService"/> can skip this candidate and
/// advance immediately to the next in FIFO order (AC-3).
///
/// This service never throws.
/// </summary>
public sealed class WaitlistOfferNotificationService : IWaitlistOfferNotificationService
{
    private readonly IEmailService                            _emailService;
    private readonly ISmsTransport                            _smsTransport;
    private readonly ApplicationDbContext                     _db;
    private readonly ILogger<WaitlistOfferNotificationService> _logger;

    public WaitlistOfferNotificationService(
        IEmailService                            emailService,
        ISmsTransport                            smsTransport,
        ApplicationDbContext                     db,
        ILogger<WaitlistOfferNotificationService> logger)
    {
        _emailService = emailService;
        _smsTransport = smsTransport;
        _db           = db;
        _logger       = logger;
    }

    /// <inheritdoc/>
    public async Task<WaitlistOfferNotificationResult> SendOfferAsync(
        WaitlistOfferNotificationRequest request,
        CancellationToken cancellationToken = default)
    {
        var emailSent         = false;
        var emailBounced      = false;
        var smsSent           = false;
        var smsSkippedOptOut  = false;
        var smsInvalidNumber  = false;

        // ── 1. Send offer email ───────────────────────────────────────────────
        try
        {
            await _emailService.SendWaitlistOfferEmailAsync(
                request.PatientEmail,
                request.PatientFullName,
                request.ClaimLink,
                request.AppointmentDetails,
                request.IsWithin24Hours,
                cancellationToken);

            emailSent = true;

            _logger.LogInformation(
                "[WAITLIST-OFFER-EMAIL] Email sent for entry {EntryId} slot {SlotId}. " +
                "Correlation: {CorrelationId}",
                request.WaitlistEntryId, request.SlotId, request.CorrelationId ?? "N/A");
        }
        catch (Exception ex)
        {
            // Classify as bounce when the exception message indicates permanent rejection;
            // any other exception is a transient transport failure (not invalid contact).
            emailBounced = IsPermanentEmailRejection(ex);

            _logger.LogError(ex,
                "[WAITLIST-OFFER-EMAIL] Failed to send offer email for entry {EntryId} slot {SlotId}. " +
                "Bounced={Bounced} Correlation: {CorrelationId}",
                request.WaitlistEntryId, request.SlotId, emailBounced, request.CorrelationId ?? "N/A");
        }

        // ── 2. Send offer SMS ─────────────────────────────────────────────────
        //    Skip when phone number is absent — treat as "not applicable" not as invalid.
        if (string.IsNullOrWhiteSpace(request.PatientPhoneNumber))
        {
            smsInvalidNumber = true; // No channel available via SMS
            _logger.LogDebug(
                "[WAITLIST-OFFER-SMS] No phone number for entry {EntryId} — SMS skipped.",
                request.WaitlistEntryId);
        }
        else
        {
            // 2a. Consult patient opt-out preference (AC-3, EC-2)
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
                // Fail closed: cannot confirm opt-in — skip SMS as safe default.
                _logger.LogError(ex,
                    "[WAITLIST-OFFER-SMS] Failed to read SMS opt-out for patient {PatientId} " +
                    "(entry {EntryId}). Skipping SMS. Correlation: {CorrelationId}",
                    request.PatientId, request.WaitlistEntryId, request.CorrelationId ?? "N/A");

                // Not an invalid-contact outcome — treat as transient failure, email may still succeed.
                goto SmsSkipLabel;
            }

            if (optedOut)
            {
                smsSkippedOptOut = true;
                _logger.LogInformation(
                    "[WAITLIST-OFFER-SMS] SMS skipped for entry {EntryId} — patient opted out. " +
                    "Correlation: {CorrelationId}",
                    request.WaitlistEntryId, request.CorrelationId ?? "N/A");
            }
            else
            {
                // 2b. Compose and dispatch SMS via transport
                try
                {
                    var body    = BuildSmsBody(request);
                    var message = new SmsTransportMessage(request.PatientPhoneNumber, body);
                    var result  = await _smsTransport.SendAsync(message, cancellationToken);

                    smsSent          = result.IsSuccess;
                    smsInvalidNumber = result.Outcome == SmsDeliveryOutcome.InvalidNumber;

                    _logger.LogInformation(
                        "[WAITLIST-OFFER-SMS] SMS outcome={Outcome} for entry {EntryId} slot {SlotId}. " +
                        "Correlation: {CorrelationId}",
                        result.Outcome, request.WaitlistEntryId, request.SlotId,
                        request.CorrelationId ?? "N/A");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[WAITLIST-OFFER-SMS] Transport error for entry {EntryId}. " +
                        "Correlation: {CorrelationId}",
                        request.WaitlistEntryId, request.CorrelationId ?? "N/A");
                }
            }
        }

        SmsSkipLabel:
        // ── 3. Derive invalid-contact outcome (EC-1) ──────────────────────────
        // Both channels confirm the contact details are permanently unreachable.
        var isInvalidContact = emailBounced && (smsInvalidNumber || smsSkippedOptOut is false && !smsSent);

        // Narrow: only set true when email permanently bounced AND SMS was an invalid number
        // (not opted-out — opted-out is a valid contact that chose not to receive SMS).
        isInvalidContact = emailBounced && smsInvalidNumber;

        return new WaitlistOfferNotificationResult(
            EmailSent:        emailSent,
            SmsSent:          smsSent,
            SmsSkippedOptOut: smsSkippedOptOut,
            IsInvalidContact: isInvalidContact);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Classifies an email send exception as a permanent bounce (5xx SMTP rejection).
    /// This is a heuristic; the underlying SMTP client may surface different exception
    /// types depending on the provider.
    /// </summary>
    private static bool IsPermanentEmailRejection(Exception ex)
    {
        var msg = ex.Message;
        // Common SMTP permanent-rejection indicators
        return msg.Contains("550", StringComparison.OrdinalIgnoreCase)  // No such user / mailbox unavailable
            || msg.Contains("551", StringComparison.OrdinalIgnoreCase)  // User not local
            || msg.Contains("553", StringComparison.OrdinalIgnoreCase)  // Mailbox name not allowed
            || msg.Contains("InvalidAddress", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("bounce", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Composes a concise SMS body for a waitlist offer (US_036 AC-1).
    /// Adds urgency copy when the slot is within 24 hours.
    /// </summary>
    private static string BuildSmsBody(WaitlistOfferNotificationRequest request)
    {
        var urgency = request.IsWithin24Hours ? " Act fast — this slot fills quickly!" : string.Empty;
        return $"Hi {request.PatientFullName}, a slot just opened: {request.AppointmentDetails}. " +
               $"Claim it here: {request.ClaimLink}{urgency} " +
               $"This offer expires in 24 hours.";
    }
}
