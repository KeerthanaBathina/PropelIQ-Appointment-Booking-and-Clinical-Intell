namespace UPACIP.Service.Notifications;

/// <summary>
/// Contract for generating scannable QR code payloads for appointment check-in (US_034 AC-2).
///
/// Implementations produce a PNG byte array that <see cref="IPdfConfirmationService"/>
/// embeds directly in the confirmation document.  Callers must not pass patient PII
/// (name, phone, email) into <see cref="BuildCheckInPayload"/>; only the booking
/// reference and appointment identifiers are encoded so the QR image is safe to
/// share and display publicly at a kiosk.
/// </summary>
public interface IQrCodeService
{
    /// <summary>
    /// Generates a PNG-encoded QR code image for the given <paramref name="payload"/>.
    /// </summary>
    /// <param name="payload">Text to encode — must not contain patient PII.</param>
    /// <param name="pixelsPerModule">
    /// QR module size in pixels.  Defaults to 10, yielding approximately 370 × 370 px
    /// at error-correction level M.
    /// </param>
    /// <returns>Raw PNG bytes of the generated QR code.</returns>
    byte[] GeneratePng(string payload, int pixelsPerModule = 10);

    /// <summary>
    /// Builds a structured, PII-free check-in payload string for the given appointment.
    /// The encoded string contains only the booking reference, appointment ID, and
    /// appointment time — no patient name, email, or phone number.
    /// </summary>
    /// <param name="appointmentId">Appointment primary key.</param>
    /// <param name="bookingReference">Short human-readable booking reference (e.g. "BK-20260422-X7R2KP").</param>
    /// <param name="appointmentTimeUtc">Scheduled UTC time of the appointment.</param>
    /// <returns>A compact check-in string suitable for QR encoding and kiosk scanning.</returns>
    string BuildCheckInPayload(Guid appointmentId, string bookingReference, DateTime appointmentTimeUtc);
}
