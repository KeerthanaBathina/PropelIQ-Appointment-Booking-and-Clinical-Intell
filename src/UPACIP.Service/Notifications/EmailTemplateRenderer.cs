using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Produces composed email subject + body pairs for each <see cref="NotificationType"/>
/// by substituting appointment-context variables into inline templates.
///
/// Design decisions:
/// <list type="bullet">
///   <item>
///     No external template files — templates are co-located with the renderer so there
///     are no file-path or hot-reload complications in the deployed service (KISS principle).
///   </item>
///   <item>
///     All substitutions use <c>string.Create</c> / interpolation — no reflection,
///     no third-party template engine required for these fixed-variable templates.
///   </item>
///   <item>
///     HTML output is kept minimal and inline-styled for maximum email-client compatibility.
///     Recipient name and provider name are HTML-encoded to prevent injection (OWASP A03).
///   </item>
///   <item>
///     This class is pure (stateless); it is registered as a singleton to avoid allocation
///     on every request.
///   </item>
/// </list>
/// </summary>
public sealed class EmailTemplateRenderer
{
    // Brand colour used consistently across all email templates
    private const string BrandBlue = "#1976D2";

    /// <summary>
    /// Renders a subject line and both HTML and plain-text bodies for the given
    /// <paramref name="request"/> and notification type.
    /// </summary>
    /// <param name="request">Appointment context with patient and appointment values.</param>
    /// <param name="timeZoneId">
    /// IANA time zone identifier used to localise <c>AppointmentTime</c>
    /// (e.g. <c>"America/New_York"</c>).
    /// </param>
    /// <returns>A <see cref="RenderedEmail"/> containing subject, HTML, and plain-text.</returns>
    public RenderedEmail Render(NotificationEmailRequest request, string timeZoneId)
    {
        var localTime = ConvertToLocalTime(request.AppointmentTime, timeZoneId);
        var dateStr   = localTime.ToString("dddd, MMMM d, yyyy");
        var timeStr   = localTime.ToString("h:mm tt");
        var provider  = string.IsNullOrWhiteSpace(request.ProviderName)
            ? "Your Provider"
            : request.ProviderName;
        var apptType  = string.IsNullOrWhiteSpace(request.AppointmentType)
            ? "Appointment"
            : request.AppointmentType;
        var bookingRef = request.BookingReference ?? "N/A";

        return request.NotificationType switch
        {
            NotificationType.Confirmation                 => BuildConfirmation(request, dateStr, timeStr, provider, apptType, bookingRef),
            NotificationType.Reminder24h                  => BuildReminder(request, dateStr, timeStr, provider, "Tomorrow"),
            NotificationType.Reminder2h                   => BuildReminder(request, dateStr, timeStr, provider, "In 2 Hours"),
            NotificationType.WaitlistOffer                => BuildWaitlistOffer(request, dateStr, timeStr, provider),
            NotificationType.SlotSwapCompleted            => BuildSlotSwapCompleted(request, dateStr, timeStr, provider),
            NotificationType.SlotSwapManualConfirmation   => BuildSlotSwapManualConfirmation(request, dateStr, timeStr, provider),
            _ => throw new ArgumentOutOfRangeException(nameof(request),
                     $"No template defined for NotificationType '{request.NotificationType}'."),
        };
    }

    // -------------------------------------------------------------------------
    // Template builders — each returns subject + HTML + plain text
    // -------------------------------------------------------------------------

    private static RenderedEmail BuildConfirmation(
        NotificationEmailRequest req,
        string dateStr,
        string timeStr,
        string provider,
        string apptType,
        string bookingRef)
    {
        var name    = Encode(req.PatientName);
        var subject = req.IsAlreadyCancelled
            ? $"Appointment Cancelled – {dateStr} at {timeStr}"
            : $"Appointment Confirmed – {dateStr} at {timeStr}";

        // Cancelled-note banner when cancellation overtook confirmation (EC-2)
        var cancelledBanner = req.IsAlreadyCancelled ? """
            <div style="background:#FFEBEE;border-left:4px solid #C62828;padding:12px 16px;margin-bottom:16px;border-radius:4px">
              <strong style="color:#C62828">Notice: This appointment has been cancelled.</strong><br>
              If you believe this is an error, please contact the clinic.
            </div>
            """ : string.Empty;

        var cancelledTextBanner = req.IsAlreadyCancelled
            ? "\n*** NOTICE: This appointment has been cancelled. ***\n"
            : string.Empty;

        // Prefilled cancellation link (AC-3) — only shown for non-cancelled confirmations
        var cancelLinkHtml = (!req.IsAlreadyCancelled && !string.IsNullOrWhiteSpace(req.CancellationLink))
            ? $"""<p><a href="{Encode(req.CancellationLink)}" style="color:{BrandBlue}">Cancel this appointment</a> (available up to 24 hours before your appointment).</p>"""
            : string.Empty;

        var cancelLinkText = (!req.IsAlreadyCancelled && !string.IsNullOrWhiteSpace(req.CancellationLink))
            ? $"\nCancel link: {req.CancellationLink}\n(Available up to 24 hours before your appointment.)"
            : string.Empty;

        var html = $"""
            {HtmlHeader(req.IsAlreadyCancelled ? "Appointment Cancelled" : "Appointment Confirmed")}
            {cancelledBanner}
            <p>Hi {name},</p>
            <p>Your <strong>{Encode(apptType)}</strong> appointment has been {(req.IsAlreadyCancelled ? "cancelled" : "confirmed")}.</p>
            {AppointmentBlock(dateStr, timeStr, Encode(provider), bookingRef)}
            {cancelLinkHtml}
            <p>Please arrive 10 minutes early for check-in.</p>
            {HtmlFooter()}
            """;

        var text = $"""
            Hi {req.PatientName},{cancelledTextBanner}

            Your {apptType} appointment is {(req.IsAlreadyCancelled ? "cancelled" : "confirmed")}.

            Date:     {dateStr}
            Time:     {timeStr}
            Provider: {provider}
            Ref:      {bookingRef}{cancelLinkText}

            Please arrive 10 minutes early.
            """;

        return new RenderedEmail(subject, html, text);
    }

    private static RenderedEmail BuildReminder(
        NotificationEmailRequest req,
        string dateStr,
        string timeStr,
        string provider,
        string timeLabel)
    {
        var name    = Encode(req.PatientName);
        var subject = $"Appointment Reminder – {timeLabel} ({dateStr} at {timeStr})";

        var html = $"""
            {HtmlHeader("Appointment Reminder")}
            <p>Hi {name},</p>
            <p>This is a reminder about your upcoming appointment.</p>
            {AppointmentBlock(dateStr, timeStr, Encode(provider), null)}
            <p>Need to cancel? You can do so up to 24 hours before your appointment
               by visiting your account dashboard.</p>
            {HtmlFooter()}
            """;

        var text = $"""
            Hi {req.PatientName},

            Reminder: you have an appointment {timeLabel.ToLowerInvariant()}.

            Date:     {dateStr}
            Time:     {timeStr}
            Provider: {provider}
            """;

        return new RenderedEmail(subject, html, text);
    }

    private static RenderedEmail BuildWaitlistOffer(
        NotificationEmailRequest req,
        string dateStr,
        string timeStr,
        string provider)
    {
        var name    = Encode(req.PatientName);
        var subject = $"A slot is available – {dateStr} at {timeStr}";

        var html = $"""
            {HtmlHeader("Slot Available")}
            <p>Hi {name},</p>
            <p>A slot matching your waitlist preferences has become available.
               Please log in to accept the offer before it expires.</p>
            {AppointmentBlock(dateStr, timeStr, Encode(provider), null)}
            {HtmlFooter()}
            """;

        var text = $"""
            Hi {req.PatientName},

            A slot on your waitlist is available:

            Date:     {dateStr}
            Time:     {timeStr}
            Provider: {provider}

            Log in to accept the offer before it expires.
            """;

        return new RenderedEmail(subject, html, text);
    }

    private static RenderedEmail BuildSlotSwapCompleted(
        NotificationEmailRequest req,
        string dateStr,
        string timeStr,
        string provider)
    {
        var name    = Encode(req.PatientName);
        var subject = $"Your appointment has been moved – {dateStr} at {timeStr}";

        var html = $"""
            {HtmlHeader("Appointment Moved")}
            <p>Hi {name},</p>
            <p>Your appointment has been automatically moved to a preferred slot.</p>
            {AppointmentBlock(dateStr, timeStr, Encode(provider), null)}
            <p>If you did not request this change, please contact the clinic.</p>
            {HtmlFooter()}
            """;

        var text = $"""
            Hi {req.PatientName},

            Your appointment has been moved to a preferred slot:

            Date:     {dateStr}
            Time:     {timeStr}
            Provider: {provider}
            """;

        return new RenderedEmail(subject, html, text);
    }

    private static RenderedEmail BuildSlotSwapManualConfirmation(
        NotificationEmailRequest req,
        string dateStr,
        string timeStr,
        string provider)
    {
        var name    = Encode(req.PatientName);
        var subject = $"Confirm your preferred slot – {dateStr} at {timeStr}";

        var html = $"""
            {HtmlHeader("Preferred Slot Available")}
            <p>Hi {name},</p>
            <p>A preferred slot has opened. Please log in and confirm whether you
               would like to move your appointment.</p>
            {AppointmentBlock(dateStr, timeStr, Encode(provider), null)}
            {HtmlFooter()}
            """;

        var text = $"""
            Hi {req.PatientName},

            A preferred slot is available:

            Date:     {dateStr}
            Time:     {timeStr}
            Provider: {provider}

            Log in to confirm or decline.
            """;

        return new RenderedEmail(subject, html, text);
    }

    // -------------------------------------------------------------------------
    // HTML helpers
    // -------------------------------------------------------------------------

    private static string HtmlHeader(string heading) => $"""
        <!DOCTYPE html>
        <html lang="en">
        <head><meta charset="UTF-8"><meta name="viewport" content="width=device-width,initial-scale=1"></head>
        <body style="font-family:Arial,sans-serif;background:#F5F5F5;padding:24px">
          <div style="max-width:520px;margin:0 auto;background:#fff;border-radius:8px;padding:32px;box-shadow:0 1px 4px rgba(0,0,0,.1)">
            <h1 style="color:{BrandBlue};font-size:1.5rem;margin-bottom:4px">UPACIP</h1>
            <h2 style="font-weight:400;font-size:1.25rem">{heading}</h2>
        """;

    private static string AppointmentBlock(
        string dateStr,
        string timeStr,
        string provider,
        string? bookingRef)
    {
        var refRow = bookingRef is null ? string.Empty
            : $"<tr><td style='padding:4px 8px;color:#616161'>Reference</td><td style='padding:4px 8px'><strong>{bookingRef}</strong></td></tr>";

        return $"""
            <table style="border-collapse:collapse;margin:16px 0;width:100%">
              <tr><td style="padding:4px 8px;color:#616161">Date</td><td style="padding:4px 8px"><strong>{dateStr}</strong></td></tr>
              <tr><td style="padding:4px 8px;color:#616161">Time</td><td style="padding:4px 8px"><strong>{timeStr}</strong></td></tr>
              <tr><td style="padding:4px 8px;color:#616161">Provider</td><td style="padding:4px 8px"><strong>{provider}</strong></td></tr>
              {refRow}
            </table>
            """;
    }

    private static string HtmlFooter() => """
          <p style="font-size:.875rem;color:#757575;margin-top:24px">
            If you have questions, please contact the clinic directly.
          </p>
          </div>
        </body>
        </html>
        """;

    /// <summary>HTML-encodes a value to prevent injection (OWASP A03).</summary>
    private static string Encode(string value) =>
        System.Net.WebUtility.HtmlEncode(value);

    /// <summary>Converts UTC <paramref name="utcTime"/> to the clinic's local time zone.</summary>
    private static DateTime ConvertToLocalTime(DateTime utcTime, string timeZoneId)
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.SpecifyKind(utcTime, DateTimeKind.Utc),
                tz);
        }
        catch (TimeZoneNotFoundException)
        {
            // Fall back to UTC when IANA ID is not supported on this OS
            return utcTime;
        }
    }
}

/// <summary>
/// A fully-rendered email composed by <see cref="EmailTemplateRenderer"/>.
/// </summary>
/// <param name="Subject">Email subject line.</param>
/// <param name="HtmlBody">HTML body content.</param>
/// <param name="PlainTextBody">Plain-text fallback body.</param>
public sealed record RenderedEmail(
    string Subject,
    string HtmlBody,
    string PlainTextBody);
