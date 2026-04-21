namespace UPACIP.Service.Appointments;

/// <summary>
/// Internal result returned by <see cref="IAppointmentCalendarService.GetCalendarFileAsync"/>
/// when an iCalendar export is successfully generated.
/// Carries the filename, MIME type, and raw byte content so the controller can stream
/// the file to the client without further transformation.
/// </summary>
public sealed record AppointmentCalendarDownloadResponse(
    string FileName,
    string ContentType,
    byte[] FileBytes);
