namespace UPACIP.Service.Documents;

/// <summary>
/// Builds the preview model for a parsed clinical document.
///
/// Responsibilities (US_042 AC-1, AC-4, EC-1, EC-2):
///   1. Load the <see cref="UPACIP.DataAccess.Entities.ClinicalDocument"/> and verify it exists and has been parsed.
///   2. Query active (non-archived) <c>ExtractedData</c> rows for the document.
///   3. Determine whether the document format supports bounding-box overlays.
///   4. Map each extracted row to a <see cref="DocumentPreviewAnnotation"/> including
///      confidence score and source snippet for tooltip rendering (AC-4).
///   5. Return a <see cref="DocumentPreviewResponse"/> whose <c>PreviewUrl</c> points to the
///      controller-mediated stream endpoint — never the raw storage path (EC-2).
/// </summary>
public interface IDocumentPreviewService
{
    /// <summary>
    /// Builds a preview response for the document identified by <paramref name="documentId"/>.
    /// </summary>
    /// <param name="documentId">PK of the <c>ClinicalDocument</c> to preview.</param>
    /// <param name="cancellationToken">Caller-supplied cancellation token.</param>
    /// <returns>
    /// The populated <see cref="DocumentPreviewResponse"/>, or <c>null</c> when the document
    /// does not exist or has not yet been parsed.
    /// </returns>
    Task<DocumentPreviewResponse?> GetPreviewAsync(
        Guid              documentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypts and returns the raw content stream for the document at <paramref name="documentId"/>
    /// so the preview controller can serve it as a file result without leaking the storage path
    /// to the client (EC-2).
    /// </summary>
    /// <param name="documentId">PK of the <c>ClinicalDocument</c> whose content to stream.</param>
    /// <param name="cancellationToken">Caller-supplied cancellation token.</param>
    /// <returns>
    /// Tuple of (content stream, MIME content-type string, original file name), or <c>null</c>
    /// when the document does not exist.
    /// </returns>
    Task<(Stream Content, string ContentType, string FileName)?> GetContentStreamAsync(
        Guid              documentId,
        CancellationToken cancellationToken = default);
}
