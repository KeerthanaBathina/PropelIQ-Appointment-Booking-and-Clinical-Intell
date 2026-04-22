namespace UPACIP.Service.Notifications;

/// <summary>
/// Stub implementation of <see cref="IPdfConfirmationService"/> used until
/// <c>task_002_be_pdf_confirmation_and_qr_generation</c> delivers the real
/// PDF/QR generation pipeline.
///
/// Always returns <see cref="PdfConfirmationResult.Unavailable"/> so the
/// notification orchestration follows the EC-1 path: confirmation email is sent
/// without a PDF attachment and a retry is logged.
/// </summary>
public sealed class StubPdfConfirmationService : IPdfConfirmationService
{
    /// <inheritdoc/>
    public Task<PdfConfirmationResult> GenerateAsync(
        PdfConfirmationContext context,
        CancellationToken cancellationToken = default)
        => Task.FromResult(PdfConfirmationResult.Unavailable());
}
