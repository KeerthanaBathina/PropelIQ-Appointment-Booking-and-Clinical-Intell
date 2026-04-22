using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UPACIP.Api.Authorization;
using UPACIP.Api.Models;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Auth;
using UPACIP.Service.Documents;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Staff-only clinical document upload endpoint for SCR-012 (US_038).
///
/// Endpoints:
///   POST /api/documents — Multipart upload of a single clinical document with category.
///
/// Authorization: Staff or Admin role only (OWASP A01 — Broken Access Control).
///   Patient callers are rejected at the policy layer.
///
/// Upload constraints (AC-1, AC-5):
///   - Allowed types: PDF, DOCX, TXT, PNG, JPG/JPEG.
///   - Maximum file size: 10 MB.
///   - Server-side validation runs independently of the client (defense-in-depth).
///
/// Security (AC-2, OWASP A02):
///   - Accepted files are encrypted with AES-256 before durable storage.
///   - Server-side file paths are never returned to clients.
///
/// Atomicity (EC-1):
///   - If the database write fails after encryption, the encrypted artifact is deleted.
///   - No partial records are created on upload failure.
/// </summary>
[ApiController]
[Route("api/documents")]
[Authorize(Policy = RbacPolicies.StaffOrAdmin)]
[Produces("application/json")]
public sealed class ClinicalDocumentsController : ControllerBase
{
    private readonly IClinicalDocumentUploadService       _uploadService;
    private readonly IDocumentReplacementService          _replacementService;
    private readonly UserManager<ApplicationUser>         _userManager;
    private readonly IAuditLogService                     _auditLog;
    private readonly ILogger<ClinicalDocumentsController> _logger;

    public ClinicalDocumentsController(
        IClinicalDocumentUploadService        uploadService,
        IDocumentReplacementService           replacementService,
        UserManager<ApplicationUser>          userManager,
        IAuditLogService                      auditLog,
        ILogger<ClinicalDocumentsController>  logger)
    {
        _uploadService      = uploadService;
        _replacementService = replacementService;
        _userManager        = userManager;
        _auditLog           = auditLog;
        _logger             = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/documents
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates, encrypts, and persists a clinical document with category assignment.
    ///
    /// Accepts multipart/form-data with the following parts:
    ///   <c>file</c>       — the document file (required).
    ///   <c>patientId</c>  — target patient GUID (required).
    ///   <c>category</c>   — DocumentCategory string value (required).
    ///   <c>notes</c>      — optional staff note (optional).
    ///
    /// Returns the persisted document metadata needed by SCR-012 for the attribution row.
    /// The server-side encrypted file path is never included in the response.
    ///
    /// Returns 400 when file validation fails with a human-readable message (AC-5).
    /// Returns 404 when the specified patient does not exist.
    /// Returns 409 on optimistic concurrency conflict (retry the upload).
    /// </summary>
    [HttpPost]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 11 * 1024 * 1024)] // 11MB headroom above 10MB limit
    [ProducesResponseType(typeof(ClinicalDocumentUploadResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UploadDocument(
        [FromForm] IFormFile file,
        [FromForm] Guid      patientId,
        [FromForm] string    category,
        [FromForm] string?   notes,
        CancellationToken    cancellationToken)
    {
        // ── Resolve authenticated uploader ────────────────────────────────────
        var uploaderIdValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(uploaderIdValue) || !Guid.TryParse(uploaderIdValue, out var uploaderId))
            return Unauthorized();

        var uploader = await _userManager.FindByIdAsync(uploaderIdValue);
        if (uploader is null)
            return Unauthorized();

        // ── Parse category ────────────────────────────────────────────────────
        if (!Enum.TryParse<DocumentCategory>(category, ignoreCase: true, out var documentCategory))
        {
            return BadRequest(new ErrorResponse
            {
                StatusCode    = 400,
                Message       = $"Invalid category '{category}'. " +
                                $"Accepted values: {string.Join(", ", Enum.GetNames<DocumentCategory>())}.",
                CorrelationId = HttpContext.TraceIdentifier,
            });
        }

        // ── Validate patient exists ───────────────────────────────────────────
        // Use AsNoTracking for the existence check — only the Guid is needed.
        var patientExists = await _userManager.Users
            .AsNoTracking()
            .AnyAsync(u => u.Id == patientId, cancellationToken);

        if (!patientExists)
        {
            return NotFound(new ErrorResponse
            {
                StatusCode    = 404,
                Message       = "Patient not found.",
                CorrelationId = HttpContext.TraceIdentifier,
            });
        }

        // ── Delegate to service (unwrap IFormFile into primitives) ────────────────
        ClinicalDocumentUploadResult result;
        try
        {
            await using var stream = file.OpenReadStream();
            result = await _uploadService.UploadAsync(
                fileStream:          stream,
                fileName:            file.FileName,
                contentType:         file.ContentType,
                fileLength:          file.Length,
                patientId:           patientId,
                category:            documentCategory,
                uploaderUserId:      uploaderId,
                uploaderDisplayName: uploader.FullName,
                cancellationToken:   cancellationToken);
        }
        catch (DocumentValidationException ex)
        {
            return BadRequest(new ErrorResponse
            {
                StatusCode    = 400,
                Message       = ex.Message,
                CorrelationId = HttpContext.TraceIdentifier,
            });
        }
        catch (DbUpdateException)
        {
            // Logged inside the service. Surface a generic 500 — detail suppressed (OWASP A05).
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                StatusCode    = 500,
                Message       = "Document upload failed due to a server error. Please try again.",
                CorrelationId = HttpContext.TraceIdentifier,
            });
        }

        // ── Audit log upload event (AC-4, US_038) ─────────────────────────────
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = HttpContext.Request.Headers.UserAgent.ToString();

        await _auditLog.LogAsync(
            AuditAction.DocumentUploaded,
            uploaderId,
            resourceType: "ClinicalDocument",
            ipAddress:    ipAddress,
            userAgent:    userAgent,
            resourceId:   result.DocumentId,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Document upload audit recorded. DocumentId={DocumentId} UploadedBy={UploaderId}",
            result.DocumentId, uploaderId);

        var response = new ClinicalDocumentUploadResponse
        {
            DocumentId     = result.DocumentId,
            FileName       = result.FileName,
            Category       = result.Category,
            UploadedAt     = result.UploadedAt,
            UploadedByName = result.UploadedByName,
            Status         = result.Status,
        };

        return CreatedAtAction(nameof(UploadDocument), new { id = response.DocumentId }, response);
    }

    // ───────────────────────────────────────────────────────────────────────────
    // POST /api/documents/{id}/replace
    // ───────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Accepts a replacement file for an existing parsed document (US_042 AC-2).
    ///
    /// The replacement is stored, linked to the previous version, and enqueued for AI parsing.
    /// The previous document version remains active until the replacement pipeline completes
    /// successfully (EC-1). Activation and archival happen automatically via the extraction
    /// persistence pipeline (AC-3).
    ///
    /// Returns 201 Created with the new document ID and version number on success.
    /// Returns 400 for format/size validation failures.
    /// Returns 404 when the target document does not exist.
    /// Returns 409 when the target document is already superseded by another replacement.
    /// </summary>
    [HttpPost("{id:guid}/replace")]
    [DisableRequestSizeLimit]
    [RequestFormLimits(MultipartBodyLengthLimit = 11 * 1024 * 1024)]
    [ProducesResponseType(typeof(ClinicalDocumentReplacementResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReplaceDocument(
        [FromRoute] Guid      id,
        [FromForm]  IFormFile file,
        [FromForm]  Guid      patientId,
        [FromForm]  string    category,
        [FromForm]  string?   notes,
        CancellationToken     cancellationToken)
    {
        // ── Resolve authenticated uploader ──────────────────────────────────
        var uploaderIdValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(uploaderIdValue) || !Guid.TryParse(uploaderIdValue, out var uploaderId))
            return Unauthorized();

        var uploader = await _userManager.FindByIdAsync(uploaderIdValue);
        if (uploader is null)
            return Unauthorized();

        // ── Parse category ──────────────────────────────────────────────
        if (!Enum.TryParse<DocumentCategory>(category, ignoreCase: true, out var documentCategory))
        {
            return BadRequest(new ErrorResponse
            {
                StatusCode    = 400,
                Message       = $"Invalid category '{category}'. " +
                                $"Accepted values: {string.Join(", ", Enum.GetNames<DocumentCategory>())}.",
                CorrelationId = HttpContext.TraceIdentifier,
            });
        }

        // ── Delegate to replacement service ─────────────────────────────────
        DocumentReplacementResult result;
        try
        {
            await using var stream = file.OpenReadStream();
            result = await _replacementService.StartReplacementAsync(
                previousDocumentId:  id,
                fileStream:          stream,
                fileName:            file.FileName,
                contentType:         file.ContentType,
                fileLength:          file.Length,
                patientId:           patientId,
                category:            documentCategory,
                uploaderUserId:      uploaderId,
                uploaderDisplayName: uploader.FullName,
                cancellationToken:   cancellationToken);
        }
        catch (DocumentValidationException ex)
        {
            return BadRequest(new ErrorResponse
            {
                StatusCode    = 400,
                Message       = ex.Message,
                CorrelationId = HttpContext.TraceIdentifier,
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found") || ex.Message.Contains("not belong"))
        {
            return NotFound(new ErrorResponse
            {
                StatusCode    = 404,
                Message       = ex.Message,
                CorrelationId = HttpContext.TraceIdentifier,
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already superseded"))
        {
            return Conflict(new ErrorResponse
            {
                StatusCode    = 409,
                Message       = ex.Message,
                CorrelationId = HttpContext.TraceIdentifier,
            });
        }
        catch (DbUpdateException)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new ErrorResponse
            {
                StatusCode    = 500,
                Message       = "Document replacement failed due to a server error. Please try again.",
                CorrelationId = HttpContext.TraceIdentifier,
            });
        }

        // ── Audit ─────────────────────────────────────────────────────────────────
        await _auditLog.LogAsync(
            AuditAction.DocumentUploaded,
            uploaderId,
            resourceType:      "ClinicalDocument",
            ipAddress:         HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            userAgent:         HttpContext.Request.Headers.UserAgent.ToString(),
            resourceId:        result.NewDocumentId,
            cancellationToken: cancellationToken);

        var response = new ClinicalDocumentReplacementResponse
        {
            NewDocumentId      = result.NewDocumentId,
            PreviousDocumentId = result.PreviousDocumentId,
            VersionNumber      = result.VersionNumber,
            FileName           = result.FileName,
            Category           = result.Category,
            UploadedAt         = result.UploadedAt,
            UploadedByName     = result.UploadedByName,
            Status             = result.Status,
        };

        return CreatedAtAction(nameof(UploadDocument), new { id = response.NewDocumentId }, response);
    }
}
