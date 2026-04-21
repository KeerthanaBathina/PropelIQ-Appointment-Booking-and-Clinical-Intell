using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Implements iCalendar (.ics) export for confirmed patient appointments (US_025, FR-025, TR-026).
///
/// RFC 5545 compliance:
///   - VCALENDAR, VEVENT, and VTIMEZONE components are always emitted.
///   - DTSTAMP is expressed in UTC (YYYYMMDDTHHMMSSZ).
///   - DTSTART / DTEND are expressed with the TZID parameter in the clinic timezone.
///   - Text fields (SUMMARY, DESCRIPTION, LOCATION) are escaped per § 3.3.11.
///   - Long property values are folded at 75 octets (CRLF + single SPACE) per § 3.1.
///   - UID is derived from the stable appointment UUID so regenerated exports update
///     the same calendar entry rather than duplicating it (AC-3).
///
/// Google Calendar and Outlook compatibility (AC-2):
///   - METHOD:PUBLISH is set so the file is treated as an informational event, not a meeting request.
///   - VTIMEZONE is included with STANDARD and DAYLIGHT sub-components derived from the
///     .NET TimeZoneInfo adjustment rules for the configured clinic timezone (EC-2).
///
/// Ownership enforcement (OWASP A01):
///   - Patient is always resolved from the JWT email claim — never from the request body.
///   - Returns null (→ 404) for appointments not owned by the requesting patient.
///
/// Logging (NFR-035):
///   - Structured Serilog events for export attempts, ownership rejections, and status rejections.
///   - Email and patient ID are excluded from log messages; only appointment ID is logged.
/// </summary>
public sealed class AppointmentCalendarService : IAppointmentCalendarService
{
    private const string ContentType   = "text/calendar";
    private const string FileNamePrefix = "appointment-";

    private readonly ApplicationDbContext                     _db;
    private readonly ClinicSettings                           _clinicSettings;
    private readonly ILogger<AppointmentCalendarService>      _logger;

    public AppointmentCalendarService(
        ApplicationDbContext                  db,
        ClinicSettings                        clinicSettings,
        ILogger<AppointmentCalendarService>   logger)
    {
        _db             = db;
        _clinicSettings = clinicSettings;
        _logger         = logger;
    }

    /// <inheritdoc/>
    public async Task<AppointmentCalendarDownloadResponse?> GetCalendarFileAsync(
        Guid              appointmentId,
        string            userEmail,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Calendar export requested: appointmentId={AppointmentId}.",
            appointmentId);

        // ── 1. Resolve patient from email (OWASP A01 — never trust client-supplied ID) ──
        var patient = await _db.Patients
            .AsNoTracking()
            .Where(p => p.Email == userEmail && p.DeletedAt == null)
            .Select(p => new { p.Id })
            .FirstOrDefaultAsync(cancellationToken);

        if (patient is null)
        {
            _logger.LogWarning(
                "Calendar export denied — no active patient record for the provided email claim.");
            return null;
        }

        // ── 2. Load the appointment, scoped to the owning patient (OWASP A01 IDOR guard) ──
        var appt = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.Id == appointmentId && a.PatientId == patient.Id)
            .Select(a => new
            {
                a.Id,
                a.AppointmentTime,
                a.Status,
                a.ProviderName,
                a.AppointmentType,
                a.Version,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (appt is null)
        {
            // Returns null for both "not found" and "belongs to another patient" — callers
            // map this to 404 so cross-patient existence is not leaked (OWASP A01).
            _logger.LogWarning(
                "Calendar export denied: appointmentId={AppointmentId} — not found or not owned.",
                appointmentId);
            return null;
        }

        // ── 3. Status guard — only Scheduled appointments may be exported ───
        if (appt.Status != AppointmentStatus.Scheduled)
        {
            _logger.LogInformation(
                "Calendar export skipped: appointmentId={AppointmentId} has status={Status}.",
                appointmentId, appt.Status);
            return null;
        }

        // ── 4. Resolve clinic timezone ────────────────────────────────────────
        TimeZoneInfo clinicTz;
        try
        {
            clinicTz = TimeZoneInfo.FindSystemTimeZoneById(_clinicSettings.TimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            _logger.LogError(
                "Calendar export failed: ClinicSettings.TimeZoneId '{TzId}' is not a valid timezone ID. " +
                "Falling back to UTC.",
                _clinicSettings.TimeZoneId);
            clinicTz = TimeZoneInfo.Utc;
        }

        // ── 5. Build .ics content ─────────────────────────────────────────────
        var icsContent = BuildIcsContent(
            appointmentId:   appt.Id,
            appointmentTime: appt.AppointmentTime,
            providerName:    appt.ProviderName,
            appointmentType: appt.AppointmentType,
            version:         appt.Version,
            clinicTz:        clinicTz);

        var fileBytes = Encoding.UTF8.GetBytes(icsContent);
        var fileName  = $"{FileNamePrefix}{appt.Id:N}.ics";

        _logger.LogInformation(
            "Calendar export generated: appointmentId={AppointmentId}, file={FileName}, bytes={ByteCount}.",
            appointmentId, fileName, fileBytes.Length);

        return new AppointmentCalendarDownloadResponse(fileName, ContentType, fileBytes);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // iCal builder helpers (RFC 5545)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the full iCalendar text payload including VCALENDAR, VTIMEZONE, and VEVENT
    /// components.  All line endings are CRLF as required by RFC 5545 § 3.1.
    /// </summary>
    private string BuildIcsContent(
        Guid        appointmentId,
        DateTime    appointmentTime,
        string?     providerName,
        string?     appointmentType,
        int         version,
        TimeZoneInfo clinicTz)
    {
        // Stable UID — derived from appointment identity, not from the appointment time,
        // so regenerated exports update rather than duplicate the calendar entry (AC-3).
        var uid      = $"{appointmentId:N}@{_clinicSettings.Domain}";
        var dtstamp  = FormatUtc(DateTime.UtcNow);
        var tzid     = clinicTz.Id;

        // Convert appointment time from UTC storage to clinic-local time (EC-2).
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(appointmentTime, clinicTz);
        var localEnd   = localStart.AddHours(1);

        var summary     = EscapeText(
            $"Appointment with {(string.IsNullOrWhiteSpace(providerName) ? "Your Provider" : providerName)}");
        var description = EscapeText(
            $"Appointment Type: {(string.IsNullOrWhiteSpace(appointmentType) ? "Medical Appointment" : appointmentType)}");
        var location = EscapeText(_clinicSettings.Name);

        var sb = new StringBuilder(1024);

        sb.Append("BEGIN:VCALENDAR\r\n");
        sb.Append("VERSION:2.0\r\n");
        sb.Append("PRODID:-//UPACIP//Appointment Booking//EN\r\n");
        sb.Append("CALSCALE:GREGORIAN\r\n");
        // METHOD:PUBLISH — informational event; no meeting-request workflow (Google / Outlook AC-2).
        sb.Append("METHOD:PUBLISH\r\n");

        // VTIMEZONE — required so clients without native IANA support can localise times (EC-2).
        sb.Append(BuildVTimezone(clinicTz));

        sb.Append("BEGIN:VEVENT\r\n");
        sb.Append($"UID:{uid}\r\n");
        sb.Append($"DTSTAMP:{dtstamp}\r\n");
        sb.Append($"DTSTART;TZID={tzid}:{FormatLocal(localStart)}\r\n");
        sb.Append($"DTEND;TZID={tzid}:{FormatLocal(localEnd)}\r\n");
        // SEQUENCE mirrors the EF Version token so reschedule increments update the existing entry.
        sb.Append($"SEQUENCE:{version}\r\n");
        sb.Append("STATUS:CONFIRMED\r\n");
        AppendFolded(sb, "SUMMARY", summary);
        AppendFolded(sb, "DESCRIPTION", description);
        AppendFolded(sb, "LOCATION", location);
        sb.Append("END:VEVENT\r\n");

        sb.Append("END:VCALENDAR\r\n");

        return sb.ToString();
    }

    /// <summary>
    /// Builds a VTIMEZONE component for the supplied <paramref name="tz"/> using its
    /// current adjustment rule.  Includes DAYLIGHT and STANDARD sub-components when
    /// DST transitions exist; falls back to a fixed-offset STANDARD-only block for UTC
    /// or fixed-offset zones.
    /// </summary>
    private static string BuildVTimezone(TimeZoneInfo tz)
    {
        var sb = new StringBuilder(512);
        sb.Append("BEGIN:VTIMEZONE\r\n");
        sb.Append($"TZID:{tz.Id}\r\n");

        var now  = DateTime.UtcNow;
        var rule = Array.Find(
            tz.GetAdjustmentRules(),
            r => r.DateStart <= now && r.DateEnd >= now);

        if (rule is not null)
        {
            var stdOffset = tz.BaseUtcOffset;
            var dstOffset = tz.BaseUtcOffset + rule.DaylightDelta;

            // DAYLIGHT sub-component (clocks spring forward)
            sb.Append("BEGIN:DAYLIGHT\r\n");
            sb.Append($"TZOFFSETFROM:{FormatOffset(stdOffset)}\r\n");
            sb.Append($"TZOFFSETTO:{FormatOffset(dstOffset)}\r\n");
            sb.Append($"TZNAME:{tz.DaylightName}\r\n");
            sb.Append($"DTSTART:{GetTransitionDtStart(rule.DaylightTransitionStart)}\r\n");
            sb.Append($"RRULE:{GetTransitionRRule(rule.DaylightTransitionStart)}\r\n");
            sb.Append("END:DAYLIGHT\r\n");

            // STANDARD sub-component (clocks fall back)
            sb.Append("BEGIN:STANDARD\r\n");
            sb.Append($"TZOFFSETFROM:{FormatOffset(dstOffset)}\r\n");
            sb.Append($"TZOFFSETTO:{FormatOffset(stdOffset)}\r\n");
            sb.Append($"TZNAME:{tz.StandardName}\r\n");
            sb.Append($"DTSTART:{GetTransitionDtStart(rule.DaylightTransitionEnd)}\r\n");
            sb.Append($"RRULE:{GetTransitionRRule(rule.DaylightTransitionEnd)}\r\n");
            sb.Append("END:STANDARD\r\n");
        }
        else
        {
            // No DST — single STANDARD block with a fixed offset.
            sb.Append("BEGIN:STANDARD\r\n");
            sb.Append($"TZOFFSETFROM:{FormatOffset(tz.BaseUtcOffset)}\r\n");
            sb.Append($"TZOFFSETTO:{FormatOffset(tz.BaseUtcOffset)}\r\n");
            sb.Append($"TZNAME:{tz.StandardName}\r\n");
            sb.Append("DTSTART:19700101T000000\r\n");
            sb.Append("END:STANDARD\r\n");
        }

        sb.Append("END:VTIMEZONE\r\n");
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // RFC 5545 formatting helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Formats a UTC DateTime as RFC 5545 UTC timestamp: <c>YYYYMMDDTHHmmssZ</c>.
    /// </summary>
    private static string FormatUtc(DateTime utc)
        => utc.ToString("yyyyMMdd'T'HHmmss'Z'");

    /// <summary>
    /// Formats a local (clinic-timezone) DateTime as RFC 5545 local timestamp: <c>YYYYMMDDTHHmmss</c>.
    /// The TZID parameter on the property line associates this value with the declared timezone.
    /// </summary>
    private static string FormatLocal(DateTime local)
        => local.ToString("yyyyMMdd'T'HHmmss");

    /// <summary>
    /// Formats a <see cref="TimeSpan"/> UTC offset as an RFC 5545 UTC offset value
    /// (<c>+HHmm</c> or <c>-HHmm</c>).
    /// </summary>
    private static string FormatOffset(TimeSpan offset)
    {
        var sign    = offset < TimeSpan.Zero ? "-" : "+";
        var absOff  = offset < TimeSpan.Zero ? offset.Negate() : offset;
        return $"{sign}{absOff.Hours:D2}{absOff.Minutes:D2}";
    }

    /// <summary>
    /// Produces the DTSTART value for a transition rule, using 1970 as the epoch year
    /// so it is compatible with RRULE recurrence processing (RFC 5545 § 3.8.5.3).
    /// </summary>
    private static string GetTransitionDtStart(TimeZoneInfo.TransitionTime t)
    {
        if (t.IsFixedDateRule)
        {
            return $"1970{t.Month:D2}{t.Day:D2}T{t.TimeOfDay:HHmmss}";
        }

        // Floating transition — resolve the concrete date for the 1970 epoch.
        var epochDate = FindFloatingTransitionDate(1970, t.Month, t.Week, t.DayOfWeek);
        return $"{epochDate:yyyyMMdd}T{t.TimeOfDay:HHmmss}";
    }

    /// <summary>
    /// Builds the RRULE value for a yearly transition rule (RFC 5545 § 3.8.5.3).
    /// Week == 5 is the .NET convention for "last occurrence", mapped to <c>-1</c> in iCal.
    /// </summary>
    private static string GetTransitionRRule(TimeZoneInfo.TransitionTime t)
    {
        if (t.IsFixedDateRule)
        {
            return $"FREQ=YEARLY;BYMONTH={t.Month};BYMONTHDAY={t.Day}";
        }

        var weekNo  = t.Week == 5 ? -1 : t.Week;
        var dayAbbr = t.DayOfWeek switch
        {
            DayOfWeek.Sunday    => "SU",
            DayOfWeek.Monday    => "MO",
            DayOfWeek.Tuesday   => "TU",
            DayOfWeek.Wednesday => "WE",
            DayOfWeek.Thursday  => "TH",
            DayOfWeek.Friday    => "FR",
            DayOfWeek.Saturday  => "SA",
            _                   => "SU",
        };
        return $"FREQ=YEARLY;BYDAY={weekNo}{dayAbbr};BYMONTH={t.Month}";
    }

    /// <summary>
    /// Finds the concrete calendar date of the <paramref name="week"/>th
    /// <paramref name="dayOfWeek"/> in the given <paramref name="month"/>/<paramref name="year"/>.
    /// Week == 5 is treated as the last occurrence (per .NET convention).
    /// </summary>
    private static DateTime FindFloatingTransitionDate(int year, int month, int week, DayOfWeek dayOfWeek)
    {
        if (week == 5)
        {
            // Find the last occurrence of dayOfWeek in the month.
            var lastDay = new DateTime(year, month, DateTime.DaysInMonth(year, month));
            var delta   = ((int)lastDay.DayOfWeek - (int)dayOfWeek + 7) % 7;
            return lastDay.AddDays(-delta);
        }

        // Find the first occurrence, then advance by (week - 1) weeks.
        var first      = new DateTime(year, month, 1);
        var daysUntil  = ((int)dayOfWeek - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(daysUntil + (week - 1) * 7);
    }

    /// <summary>
    /// Escapes special characters in a TEXT value field per RFC 5545 § 3.3.11:
    ///   backslash → <c>\\</c>, comma → <c>\,</c>, semicolon → <c>\;</c>,
    ///   newline (LF/CRLF) → <c>\n</c>.
    /// </summary>
    private static string EscapeText(string value)
        => value
            .Replace("\\", "\\\\")
            .Replace(",",  "\\,")
            .Replace(";",  "\\;")
            .Replace("\r\n", "\\n")
            .Replace("\n",   "\\n");

    /// <summary>
    /// Appends a property line to <paramref name="sb"/>, folding at 75 octets per
    /// RFC 5545 § 3.1 (CRLF + single SPACE continuation).
    /// Applies only when the combined <c>NAME:value</c> string exceeds 75 characters.
    /// </summary>
    private static void AppendFolded(StringBuilder sb, string name, string value)
    {
        var line   = $"{name}:{value}";
        const int maxOctets = 75;

        if (line.Length <= maxOctets)
        {
            sb.Append(line);
            sb.Append("\r\n");
            return;
        }

        // First segment: exactly maxOctets characters.
        sb.Append(line[..maxOctets]);
        sb.Append("\r\n");

        var pos = maxOctets;
        // Continuation segments: SPACE + up to 74 characters (75 - 1 for the leading space).
        while (pos < line.Length)
        {
            var segLen = Math.Min(74, line.Length - pos);
            sb.Append(' ');
            sb.Append(line.AsSpan(pos, segLen));
            sb.Append("\r\n");
            pos += segLen;
        }
    }
}
