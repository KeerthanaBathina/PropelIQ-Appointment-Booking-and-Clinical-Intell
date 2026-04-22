using QRCoder;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Generates scannable QR code images for appointment check-in (US_034 AC-2).
///
/// Uses QRCoder — a pure C# library with no native dependencies — to produce
/// PNG byte arrays that are embedded in the confirmation PDF by
/// <see cref="PdfConfirmationService"/>.
///
/// <para>
/// The check-in payload built by <see cref="BuildCheckInPayload"/> intentionally
/// omits all patient PII.  Only the booking reference, appointment ID, and
/// scheduled time are encoded so the QR image is safe to display at a public kiosk.
/// </para>
/// </summary>
public sealed class QrCodeService : IQrCodeService
{
    /// <inheritdoc/>
    public byte[] GeneratePng(string payload, int pixelsPerModule = 10)
    {
        using var generator = new QRCodeGenerator();
        using var data      = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        using var code      = new PngByteQRCode(data);
        return code.GetGraphic(pixelsPerModule);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Payload format (URI-style, no PII):
    /// <c>UPACIP:checkin?ref=BK-20260422-X7R2KP&amp;appt=&lt;guid-N&gt;&amp;t=202604221430</c>
    /// </remarks>
    public string BuildCheckInPayload(
        Guid     appointmentId,
        string   bookingReference,
        DateTime appointmentTimeUtc)
    {
        // appointmentId is formatted without hyphens (:N) to keep the QR payload compact.
        // appointmentTimeUtc precision is truncated to minutes — seconds are not relevant
        // for check-in and shorter strings reduce QR density.
        return $"UPACIP:checkin?ref={bookingReference}" +
               $"&appt={appointmentId:N}" +
               $"&t={appointmentTimeUtc:yyyyMMddHHmm}";
    }
}
