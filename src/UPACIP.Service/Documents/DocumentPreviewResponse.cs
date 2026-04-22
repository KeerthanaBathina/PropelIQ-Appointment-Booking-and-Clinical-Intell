namespace UPACIP.Service.Documents;

/// <summary>
/// Full preview payload returned by <c>GET /api/documents/{id}/preview</c> (US_042 AC-1, AC-4).
///
/// The <see cref="PreviewUrl"/> is a controller-issued secure read URL (no raw storage paths
/// are included) so callers cannot reconstruct the encrypted file location (EC-2).
///
/// <see cref="SupportsOverlay"/> controls frontend rendering mode:
/// - <c>true</c>  → frontend renders image/PDF + bounding-box highlight overlay (AC-1).
/// - <c>false</c> → frontend falls back to inline text annotation view (EC-1).
/// </summary>
public sealed record DocumentPreviewResponse
{
    /// <summary>Document GUID — matches the route parameter used to request the preview.</summary>
    public Guid DocumentId { get; init; }

    /// <summary>
    /// Relative API URL used by the frontend to stream the document content.
    /// Format: <c>/api/documents/{id}/preview/content</c>.
    /// Never exposes the encrypted storage path or base directory (EC-2).
    /// </summary>
    public string PreviewUrl { get; init; } = string.Empty;

    /// <summary>
    /// MIME type of the preview content (e.g. <c>application/pdf</c>, <c>image/png</c>, <c>text/plain</c>).
    /// Used by the frontend to choose between &lt;object&gt;, &lt;img&gt;, or text rendering.
    /// </summary>
    public string ContentType { get; init; } = string.Empty;

    /// <summary>
    /// True when the document format supports bounding-box region overlays.
    /// Supported formats: PDF (<c>application/pdf</c>), PNG (<c>image/png</c>),
    /// JPG (<c>image/jpeg</c>), TIFF (<c>image/tiff</c>).
    /// False for TXT and other text-only formats (EC-1).
    /// </summary>
    public bool SupportsOverlay { get; init; }

    /// <summary>Original filename as submitted by the uploader (display only).</summary>
    public string FileName { get; init; } = string.Empty;

    /// <summary>Document category string value (e.g. LabResult, Prescription).</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// All extraction annotations for the active (non-archived) version of this document.
    /// Empty list when no extraction data exists for this document.
    /// </summary>
    public IReadOnlyList<DocumentPreviewAnnotation> Annotations { get; init; } =
        Array.Empty<DocumentPreviewAnnotation>();
}
