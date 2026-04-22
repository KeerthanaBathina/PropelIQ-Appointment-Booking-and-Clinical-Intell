using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Production implementation of <see cref="IPdfConfirmationService"/>.
///
/// Generates a booking-confirmation PDF containing patient-facing appointment
/// details, booking reference, and an embedded scannable QR code for patient
/// check-in at the clinic (US_034 AC-2).
///
/// <para><b>Cancelled-state awareness (EC-2):</b> When
/// <see cref="PdfConfirmationContext.IsAlreadyCancelled"/> is <c>true</c> (set by
/// the caller after re-reading the latest appointment status), the document shows a
/// prominent cancelled notice and omits the check-in QR code instead of displaying
/// stale "Scheduled" language.</para>
///
/// <para><b>Failure safety (EC-1):</b> Any render exception is caught and returned
/// as <see cref="PdfConfirmationResult.Failed"/> so the notification orchestration
/// can dispatch the confirmation email without the PDF attachment and queue a
/// deferred retry.  This implementation never throws.</para>
/// </summary>
public sealed class PdfConfirmationService : IPdfConfirmationService
{
    private static readonly Regex SafeFileNameRegex =
        new(@"[^A-Za-z0-9\-]", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    private readonly IQrCodeService                  _qrCodeService;
    private readonly ILogger<PdfConfirmationService> _logger;

    /// <summary>
    /// Initialises the service and configures the QuestPDF Community licence.
    /// The licence assignment is idempotent — repeated instantiations are safe.
    /// </summary>
    public PdfConfirmationService(
        IQrCodeService                  qrCodeService,
        ILogger<PdfConfirmationService> logger)
    {
        _qrCodeService = qrCodeService;
        _logger        = logger;

        // QuestPDF Community licence — must be declared before any document is rendered.
        // Idempotent: safe to call multiple times (e.g. in tests or multiple DI resolutions).
        QuestPDF.Settings.License = LicenseType.Community;
    }

    /// <inheritdoc/>
    public Task<PdfConfirmationResult> GenerateAsync(
        PdfConfirmationContext context,
        CancellationToken      cancellationToken = default)
    {
        try
        {
            var pdfBytes = RenderDocument(context);
            var fileName = BuildFileName(context.BookingReference);

            _logger.LogDebug(
                "PDF confirmation generated for appointment {AppointmentId} " +
                "(ref: {BookingReference}), size={Bytes} bytes, cancelled={IsCancelled}.",
                context.AppointmentId, context.BookingReference,
                pdfBytes.Length, context.IsAlreadyCancelled);

            return Task.FromResult(PdfConfirmationResult.Succeeded(pdfBytes, fileName));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[PDF-RETRY-NEEDED] PDF render failed for appointment {AppointmentId} " +
                "(ref: {BookingReference}). EC-1: email will be sent without attachment. " +
                "Error: {ErrorType}",
                context.AppointmentId, context.BookingReference, ex.GetType().Name);

            return Task.FromResult(
                PdfConfirmationResult.Failed($"PDF render error: {ex.GetType().Name}"));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private byte[] RenderDocument(PdfConfirmationContext context)
    {
        // Build PII-free check-in QR payload and generate PNG bytes.
        // QR generation is skipped for already-cancelled appointments (EC-2) — the
        // QR code would be meaningless for a cancelled booking and must not be shown.
        byte[]? qrPng = null;
        if (!context.IsAlreadyCancelled)
        {
            var checkInPayload = _qrCodeService.BuildCheckInPayload(
                context.AppointmentId,
                context.BookingReference,
                context.AppointmentTime);

            qrPng = _qrCodeService.GeneratePng(checkInPayload);
        }

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(t => t.FontSize(11).FontColor(Colors.Black));

                page.Content().Column(col =>
                {
                    // ── Header ────────────────────────────────────────────────
                    col.Item()
                        .PaddingBottom(20)
                        .Text("Appointment Confirmation")
                        .FontSize(22).Bold().FontColor(Colors.Blue.Medium);

                    // ── Cancelled notice (EC-2) ───────────────────────────────
                    if (context.IsAlreadyCancelled)
                    {
                        col.Item()
                            .PaddingBottom(16)
                            .Background(Colors.Red.Lighten4)
                            .Padding(10)
                            .Text(
                                "NOTICE: This appointment has been CANCELLED. " +
                                "Please contact the clinic to reschedule.")
                            .FontSize(12).Bold().FontColor(Colors.Red.Medium);
                    }

                    // ── Appointment details table ─────────────────────────────
                    col.Item().PaddingBottom(16).Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.ConstantColumn(160);
                            cols.RelativeColumn();
                        });

                        // Booking reference
                        table.Cell().PaddingBottom(6).Text("Booking Reference:").Bold();
                        table.Cell().PaddingBottom(6).Text(context.BookingReference);

                        // Patient name
                        table.Cell().PaddingBottom(6).Text("Patient Name:").Bold();
                        table.Cell().PaddingBottom(6).Text(context.PatientName);

                        // Date & time — formatted in UTC; no local timezone conversion
                        // (patient timezone localisation is handled by the email template)
                        table.Cell().PaddingBottom(6).Text("Date & Time (UTC):").Bold();
                        table.Cell().PaddingBottom(6)
                            .Text(context.AppointmentTime.ToString(
                                "dddd, dd MMMM yyyy 'at' HH:mm 'UTC'",
                                System.Globalization.CultureInfo.InvariantCulture));

                        // Provider
                        table.Cell().PaddingBottom(6).Text("Provider:").Bold();
                        table.Cell().PaddingBottom(6)
                            .Text(context.ProviderName ?? "To be assigned");

                        // Appointment type
                        table.Cell().PaddingBottom(6).Text("Appointment Type:").Bold();
                        table.Cell().PaddingBottom(6)
                            .Text(context.AppointmentType ?? "General");

                        // Status
                        table.Cell().PaddingBottom(6).Text("Status:").Bold();
                        table.Cell().PaddingBottom(6)
                            .Text(context.IsAlreadyCancelled ? "CANCELLED" : "Confirmed")
                            .FontColor(context.IsAlreadyCancelled
                                ? Colors.Red.Medium
                                : Colors.Green.Medium);
                    });

                    // ── Check-in QR code (omitted for cancelled appointments) ─
                    if (!context.IsAlreadyCancelled && qrPng is not null)
                    {
                        col.Item().Column(qrCol =>
                        {
                            qrCol.Item()
                                .PaddingBottom(4)
                                .Text("Check-In QR Code")
                                .FontSize(13).Bold();

                            qrCol.Item()
                                .PaddingBottom(6)
                                .Text(
                                    "Present this code at the reception desk or " +
                                    "self-check-in kiosk on the day of your appointment.")
                                .FontSize(10).FontColor(Colors.Grey.Medium);

                            qrCol.Item()
                                .Width(130)
                                .Height(130)
                                .Image(qrPng).FitArea();
                        });
                    }

                    // ── Footer ────────────────────────────────────────────────
                    col.Item()
                        .PaddingTop(28)
                        .BorderTop(1).BorderColor(Colors.Grey.Lighten2)
                        .PaddingTop(8)
                        .Text(
                            "This is an automated document. " +
                            "Please do not reply to this message.")
                        .FontSize(9).FontColor(Colors.Grey.Medium).Italic();
                });
            });
        }).GeneratePdf();
    }

    /// <summary>
    /// Builds a safe attachment file name from the booking reference.
    /// Characters outside <c>[A-Za-z0-9\-]</c> are replaced with underscores to
    /// prevent path-traversal or file-system injection (OWASP A03).
    /// </summary>
    private static string BuildFileName(string bookingReference)
    {
        var safeRef = SafeFileNameRegex.Replace(bookingReference, "_");
        return $"confirmation-{safeRef}.pdf";
    }
}
