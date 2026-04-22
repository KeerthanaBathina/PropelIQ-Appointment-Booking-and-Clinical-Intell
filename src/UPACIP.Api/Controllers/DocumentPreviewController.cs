using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UPACIP.Api.Authorization;
using UPACIP.Api.Models;
using UPACIP.Service.Documents;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Staff-only endpoints for document preview and secure content streaming (US_042 AC-1, AC-4, EC-1, EC-2).
///
/// Endpoints:
///   GET /api/documents/{id}/preview         — Preview metadata + extraction annotations.
///   GET /api/documents/{id}/preview/content — Decrypted document content stream.
///
/// Authorization: StaffOrAdmin policy (OWASP A01 — Broken Access Control).
///
/// Security (EC-2, OWASP A02 — Cryptographic Failures):
///   Raw encrypted storage paths are never returned to clients.
///   Content is served via a controller-mediated stream so the caller cannot reconstruct
///   the storage location or bypass the authorization layer.
///
/// Access control is enforced at the policy layer; staff callers can only preview documents
/// that exist and are in a parsed state. Missing or unparsed documents return 404.
/// </summary>
[ApiController]
[Route("api/documents")]
[Authorize(Policy = RbacPolicies.StaffOrAdmin)]
[Produces("application/json")]
public sealed class DocumentPreviewController : ControllerBase
{
    private readonly IDocumentPreviewService         _previewService;
    private readonly ILogger<DocumentPreviewController> _logger;

    public DocumentPreviewController(
        IDocumentPreviewService          previewService,
        ILogger<DocumentPreviewController> logger)
    {
        _previewService = previewService;
        _logger         = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/documents/{id}/preview
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns preview metadata and extraction annotations for a parsed document (AC-1, AC-4).
    ///
    /// The <c>previewUrl</c> in the response points to
    /// <c>GET /api/documents/{id}/preview/content</c> — never a raw file-system path (EC-2).
    ///
    /// Returns 404 when:
    ///   - The document does not exist, or
    ///   - The document has not yet been parsed (ProcessingStatus is not Completed or Failed).
    /// </summary>
    [HttpGet("{id:guid}/preview")]
    [ProducesResponseType(typeof(DocumentPreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPreview(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var preview = await _previewService.GetPreviewAsync(id, cancellationToken);

        if (preview is null)
        {
            _logger.LogInformation(
                "DocumentPreviewController: preview not available. DocumentId={DocumentId}", id);

            return NotFound(new ErrorResponse
            {
                StatusCode = 404,
                Message    = "Document preview is not available. The document may not exist or may not have been parsed yet.",
            });
        }

        return Ok(preview);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/documents/{id}/preview/content
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Decrypts and streams the document content for the authenticated staff caller (EC-2).
    ///
    /// The response is the raw document bytes with the original MIME type so the browser or
    /// frontend renderer can display it directly. The encrypted storage path is never included
    /// in the response headers or body.
    ///
    /// Returns 404 when the document does not exist.
    /// Returns 500 when the encrypted file is not found on disk (storage integrity error).
    /// </summary>
    [HttpGet("{id:guid}/preview/content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status500InternalServerError)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPreviewContent(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        (Stream Content, string ContentType, string FileName)? result;
        try
        {
            result = await _previewService.GetContentStreamAsync(id, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            _logger.LogError(
                "DocumentPreviewController: encrypted file missing. DocumentId={DocumentId}", id);

            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                StatusCode = 500,
                Message    = "The document file could not be located. Please contact support.",
            });
        }

        if (result is null)
        {
            return NotFound(new ErrorResponse
            {
                StatusCode = 404,
                Message    = "Document not found.",
            });
        }

        // Serve the decrypted bytes. FileStreamResult disposes the stream after the response
        // is fully sent, so callers do not need to dispose it manually.
        return new FileStreamResult(result.Value.Content, result.Value.ContentType)
        {
            FileDownloadName = result.Value.FileName,
            EnableRangeProcessing = true,
        };
    }
}
