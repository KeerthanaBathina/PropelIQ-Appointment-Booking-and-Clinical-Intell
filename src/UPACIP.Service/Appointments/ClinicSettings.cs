namespace UPACIP.Service.Appointments;

/// <summary>
/// Strongly-typed POCO for the <c>ClinicSettings</c> configuration section.
/// Injected as a singleton into <see cref="AppointmentCalendarService"/> so the
/// clinic name, IANA timezone ID, and domain used in iCal exports are configurable
/// without redeployment.
///
/// Register via:
///   var clinicSettings = builder.Configuration.GetSection("ClinicSettings").Get&lt;ClinicSettings&gt;() ?? new();
///   builder.Services.AddSingleton(clinicSettings);
/// </summary>
public sealed class ClinicSettings
{
    /// <summary>Human-readable clinic name used in the iCal LOCATION field (AC-1).</summary>
    public string Name { get; init; } = "UPACIP Medical Clinic";

    /// <summary>
    /// IANA timezone ID for the clinic (e.g. "America/New_York").
    /// Used to localise appointment times in the DTSTART / DTEND fields and to
    /// generate the VTIMEZONE component (AC-2, EC-2).
    /// </summary>
    public string TimeZoneId { get; init; } = "America/New_York";

    /// <summary>
    /// Domain token appended to the appointment UUID to form the globally-unique iCal UID
    /// (e.g. "upacip.clinic").  Must be a valid DNS label.  Changing this value after
    /// deployment causes regenerated .ics files to duplicate — not update — existing
    /// calendar entries, so treat it as immutable once set (AC-3).
    /// </summary>
    public string Domain { get; init; } = "upacip.clinic";
}
