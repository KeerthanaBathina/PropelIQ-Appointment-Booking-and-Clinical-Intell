namespace UPACIP.Service.Notifications;

/// <summary>
/// Contract for generating a booking-confirmation PDF attachment.
/// The PDF includes appointment details, booking reference, and a scannable QR
/// code for patient check-in (US_034 AC-2).
///
/// When generation fails, implementations MUST return
/// <see cref="PdfConfirmationResult.Failed"/> rather than throw, allowing the
/// notification layer to dispatch the email without the PDF attachment and enqueue
/// a deferred retry (EC-1).
///
/// Note: the production implementation is provided by
/// <c>task_002_be_pdf_confirmation_and_qr_generation</c>.  Until that task
/// completes, a stub implementation registers here that always returns
/// <c>PdfConfirmationResult.Unavailable</c>.
/// </summary>
public interface IPdfConfirmationService
{
    /// <summary>
    /// Generates a confirmation PDF for the appointment described by
    /// <paramref name="context"/>.
    /// </summary>
    /// <param name="context">Appointment metadata used to compose the document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A <see cref="PdfConfirmationResult"/> containing either the attachment bytes
    /// or a retryable failure indicator.  Never throws.
    /// </returns>
    Task<PdfConfirmationResult> GenerateAsync(
        PdfConfirmationContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Appointment-context payload supplied to <see cref="IPdfConfirmationService.GenerateAsync"/>.
/// </summary>
public sealed record PdfConfirmationContext(
    Guid AppointmentId,
    string PatientName,
    DateTime AppointmentTime,
    string? ProviderName,
    string? AppointmentType,
    string BookingReference,
    bool IsAlreadyCancelled);

/// <summary>
/// Result returned by <see cref="IPdfConfirmationService.GenerateAsync"/>.
/// </summary>
public sealed class PdfConfirmationResult
{
    // ── Factory helpers ──────────────────────────────────────────────────────

    /// <summary>Creates a successful result carrying the PDF attachment bytes.</summary>
    public static PdfConfirmationResult Succeeded(byte[] pdfBytes, string fileName) =>
        new(true, pdfBytes, fileName, false, null);

    /// <summary>
    /// Creates a failed result when PDF generation throws or the library is
    /// unavailable.  The notification layer should send email without attachment
    /// and enqueue a retry (EC-1).
    /// </summary>
    public static PdfConfirmationResult Failed(string reason) =>
        new(false, null, null, false, reason);

    /// <summary>
    /// Creates a result indicating the PDF service is not yet available.
    /// Used by the stub registration until <c>task_002</c> provides a real
    /// implementation (EC-1 path — email sent without PDF).
    /// </summary>
    public static PdfConfirmationResult Unavailable() =>
        new(false, null, null, true, "PDF confirmation service not yet configured.");

    // ── Properties ───────────────────────────────────────────────────────────

    /// <summary>Whether a PDF was successfully generated.</summary>
    public bool IsSuccess { get; }

    /// <summary>Raw PDF bytes.  <c>null</c> for all non-success outcomes.</summary>
    public byte[]? PdfBytes { get; }

    /// <summary>Suggested attachment file name (e.g. "confirmation-BK-20260422-X7R2KP.pdf").</summary>
    public string? FileName { get; }

    /// <summary>
    /// <c>true</c> when the PDF service is not yet configured and retry
    /// is not meaningful — email should proceed without attachment.
    /// </summary>
    public bool IsServiceUnavailable { get; }

    /// <summary>Failure reason for logging (no PII).</summary>
    public string? FailureReason { get; }

    private PdfConfirmationResult(
        bool isSuccess, byte[]? pdfBytes, string? fileName,
        bool isServiceUnavailable, string? failureReason)
    {
        IsSuccess          = isSuccess;
        PdfBytes           = pdfBytes;
        FileName           = fileName;
        IsServiceUnavailable = isServiceUnavailable;
        FailureReason      = failureReason;
    }
}
