using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Documents;

/// <summary>
/// Builds document preview models from stored clinical documents and extracted-data attribution
/// (US_042 AC-1, AC-4, EC-1, EC-2).
///
/// Overlay eligibility:
///   Formats that support bounding-box overlays: PDF, PNG, JPEG, TIFF.
///   For these formats <see cref="DocumentPreviewResponse.SupportsOverlay"/> is <c>true</c>.
///   All other formats (TXT, DOCX, etc.) return <c>false</c> and the frontend renders
///   inline text annotations instead (EC-1).
///
/// Annotation coordinates:
///   Current pipeline stores <c>ExtractionRegion</c> as a free-text label (e.g. "table row 3").
///   Bounding-box geometry is not yet produced by the extraction pipeline, so
///   <see cref="DocumentPreviewAnnotation.Bounds"/> is always <c>null</c> in this release.
///   The overlay infrastructure and DTO are forward-compatible: when a future pipeline version
///   populates coordinate metadata, only this service needs updating.
///
/// Security (EC-2):
///   <c>PreviewUrl</c> is set to the controller-mediated stream route
///   <c>/api/documents/{id}/preview/content</c>. Raw encrypted storage paths are never included
///   in the response, preventing directory traversal or key inference by clients.
///
/// Active-version filtering:
///   Only rows belonging to the specified document ID are returned.
///   Archived rows from superseded versions are excluded by the document-ID foreign key scope
///   (future document-versioning story will add explicit archive filtering).
/// </summary>
public sealed class DocumentPreviewService : IDocumentPreviewService
{
    // MIME types for which bounding-box overlay is supported.
    private static readonly HashSet<string> _overlayContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/png",
        "image/jpeg",
        "image/jpg",
        "image/tiff",
        "image/tif",
    };

    // MIME types that are considered "parsed" and safe to show as plain text.
    private static readonly HashSet<string> _textContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "text/plain",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    };

    private readonly ApplicationDbContext              _db;
    private readonly IEncryptedFileStorageService      _storage;
    private readonly ILogger<DocumentPreviewService>   _logger;

    public DocumentPreviewService(
        ApplicationDbContext              db,
        IEncryptedFileStorageService      storage,
        ILogger<DocumentPreviewService>   logger)
    {
        _db      = db;
        _storage = storage;
        _logger  = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetPreviewAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<DocumentPreviewResponse?> GetPreviewAsync(
        Guid              documentId,
        CancellationToken cancellationToken = default)
    {
        // Load document with extracted-data rows in a single query.
        var document = await _db.ClinicalDocuments
            .AsNoTracking()
            .Include(d => d.ExtractedData)
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        if (document is null)
        {
            _logger.LogWarning(
                "DocumentPreviewService: document not found. DocumentId={DocumentId}", documentId);
            return null;
        }

        // Only parsed documents have annotation data worth showing (AC-1).
        // We still serve a preview for completed documents even if extraction produced
        // no rows (e.g. no-data-extracted outcome) so the original file is viewable.
        if (document.ProcessingStatus is not (ProcessingStatus.Completed or ProcessingStatus.Failed))
        {
            _logger.LogInformation(
                "DocumentPreviewService: document not yet parsed. DocumentId={DocumentId} Status={Status}",
                documentId, document.ProcessingStatus);
            return null;
        }

        var contentType   = document.ContentType ?? "application/octet-stream";
        var supportsOverlay = _overlayContentTypes.Contains(contentType);

        // Build annotation list from all extracted-data rows for this document.
        var annotations = document.ExtractedData
            .OrderBy(e => e.PageNumber)
            .ThenBy(e => e.DataType.ToString())
            .Select(e => BuildAnnotation(e))
            .ToList();

        var previewUrl = $"/api/documents/{documentId}/preview/content";

        _logger.LogInformation(
            "DocumentPreviewService: preview built. DocumentId={DocumentId} AnnotationCount={Count} SupportsOverlay={Overlay}",
            documentId, annotations.Count, supportsOverlay);

        return new DocumentPreviewResponse
        {
            DocumentId      = documentId,
            PreviewUrl      = previewUrl,
            ContentType     = contentType,
            SupportsOverlay = supportsOverlay,
            FileName        = document.OriginalFileName,
            Category        = document.DocumentCategory.ToString(),
            Annotations     = annotations,
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetContentStreamAsync
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<(Stream Content, string ContentType, string FileName)?> GetContentStreamAsync(
        Guid              documentId,
        CancellationToken cancellationToken = default)
    {
        var document = await _db.ClinicalDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        if (document is null)
            return null;

        var stream = await _storage.ReadDecryptedStreamAsync(document.FilePath, cancellationToken);
        return (stream, document.ContentType ?? "application/octet-stream", document.OriginalFileName);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static DocumentPreviewAnnotation BuildAnnotation(UPACIP.DataAccess.Entities.ExtractedData row)
    {
        // Derive the primary display label from the extraction content.
        // Priority: NormalizedValue → RawText → DataType fallback.
        var label = row.DataContent?.NormalizedValue
            ?? row.DataContent?.RawText
            ?? row.DataType.ToString();

        // Map confidence: ReviewReason.ConfidenceUnavailable → null score (EC-1 spec, US_041 EC-1).
        float? confidenceScore = row.ReviewReason == ReviewReason.ConfidenceUnavailable
            ? null
            : row.ConfidenceScore;

        return new DocumentPreviewAnnotation
        {
            ExtractedDataId    = row.Id,
            DataType           = row.DataType.ToString(),
            Label              = label,
            ConfidenceScore    = confidenceScore,
            ReviewReason       = row.ReviewReason.ToString(),
            VerificationStatus = row.VerificationStatus.ToString(),
            PageNumber         = row.PageNumber,
            ExtractionRegion   = row.ExtractionRegion,
            SourceSnippet      = row.DataContent?.SourceSnippet,
            // Bounds are null in this release — coordinate metadata is not yet produced
            // by the extraction pipeline. Forward-compatible: populated when available.
            Bounds             = null,
        };
    }
}
