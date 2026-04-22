using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using UPACIP.Api.Authorization;
using UPACIP.Api.Models;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Auth;
using UPACIP.Service.Documents;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Staff-only endpoints for querying and verifying extracted clinical data (US_041).
///
/// Endpoints:
///   GET    /api/extracted-data?documentId={id}          — Get rows for a single document (SCR-012)
///   GET    /api/extracted-data?patientId={id}           — Get rows for all patient docs (SCR-013)
///   GET    /api/extracted-data/flagged-counts?documentIds={ids} — Remaining review counts
///   POST   /api/extracted-data/{id}/verify              — Single-row verify or correct (AC-4)
///   POST   /api/extracted-data/bulk-verify              — Bulk verify multiple rows (EC-2)
///
/// Authorization: StaffOrAdmin policy (OWASP A01 — Broken Access Control).
///
/// Audit (OWASP A09):
///   Verification actions are logged with verifier identity and row ID.
///   PHI-rich DataContent is excluded from all log payloads.
/// </summary>
[ApiController]
[Route("api/extracted-data")]
[Authorize(Policy = RbacPolicies.StaffOrAdmin)]
[Produces("application/json")]
public sealed class ExtractedDataController : ControllerBase
{
    private readonly IExtractedDataVerificationService        _verificationService;
    private readonly UserManager<ApplicationUser>             _userManager;
    private readonly IAuditLogService                         _auditLog;
    private readonly ILogger<ExtractedDataController>         _logger;

    public ExtractedDataController(
        IExtractedDataVerificationService        verificationService,
        UserManager<ApplicationUser>             userManager,
        IAuditLogService                         auditLog,
        ILogger<ExtractedDataController>         logger)
    {
        _verificationService = verificationService;
        _userManager         = userManager;
        _auditLog            = auditLog;
        _logger              = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/extracted-data
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns extracted data rows filtered by either <c>documentId</c> or <c>patientId</c>.
    /// Exactly one query parameter must be supplied.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ExtractedDataRow>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetExtractedData(
        [FromQuery] Guid?             documentId,
        [FromQuery] Guid?             patientId,
        CancellationToken             cancellationToken)
    {
        if (documentId.HasValue == patientId.HasValue)
        {
            return BadRequest(new ErrorResponse
            {
                StatusCode = 400,
                Message    = "Exactly one of 'documentId' or 'patientId' query parameters is required.",
            });
        }

        IReadOnlyList<ExtractedDataQueryRow> rows;

        if (documentId.HasValue)
        {
            rows = await _verificationService.GetByDocumentAsync(documentId.Value, cancellationToken);
        }
        else
        {
            rows = await _verificationService.GetByPatientAsync(patientId!.Value, cancellationToken);
        }

        return Ok(rows.Select(MapToResponse).ToList());
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/extracted-data/flagged-counts
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns remaining pending-review counts per document ID.
    /// Accepts a comma-delimited <c>documentIds</c> query parameter (max 50 IDs).
    /// Used by SCR-012 to refresh badge counts after verification.
    /// </summary>
    [HttpGet("flagged-counts")]
    [ProducesResponseType(typeof(FlaggedCountsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetFlaggedCounts(
        [FromQuery(Name = "documentIds")] string documentIdsRaw,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(documentIdsRaw))
        {
            return BadRequest(new ErrorResponse
            {
                StatusCode = 400,
                Message    = "'documentIds' query parameter is required.",
            });
        }

        var parts = documentIdsRaw.Split(',', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length > 50)
        {
            return BadRequest(new ErrorResponse
            {
                StatusCode = 400,
                Message    = "At most 50 document IDs may be queried at once.",
            });
        }

        var parsed = new List<Guid>(parts.Length);
        foreach (var part in parts)
        {
            if (!Guid.TryParse(part.Trim(), out var id))
            {
                return BadRequest(new ErrorResponse
                {
                    StatusCode = 400,
                    Message    = $"Invalid document ID format: '{part}'.",
                });
            }
            parsed.Add(id);
        }

        var counts = await _verificationService.GetFlaggedCountsAsync(parsed, cancellationToken);

        return Ok(new FlaggedCountsResponse { FlaggedCounts = counts });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/extracted-data/{id}/verify
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies or corrects a single extracted data row (US_041 AC-4).
    ///
    /// Returns 404 when the row does not exist or is ineligible for verification
    /// (not flagged, already verified).
    /// Returns 200 with the updated row state and refreshed flagged counts on success.
    /// </summary>
    [HttpPost("{id:guid}/verify")]
    [ProducesResponseType(typeof(ExtractedDataVerificationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VerifySingle(
        [FromRoute] Guid                    id,
        [FromBody]  VerifyExtractedDataRequest request,
        CancellationToken                   cancellationToken)
    {
        var (verifierId, verifierName) = await ResolveVerifierAsync();
        if (verifierId == Guid.Empty) return Unauthorized();

        // Guard: corrected action must supply at least one replacement field.
        if (request.Action == "corrected" &&
            request.CorrectedNormalizedValue is null &&
            request.CorrectedRawText is null &&
            request.CorrectedUnit is null)
        {
            return BadRequest(new ErrorResponse
            {
                StatusCode = 400,
                Message    = "Action 'corrected' requires at least one correction field " +
                             "(correctedNormalizedValue, correctedRawText, or correctedUnit).",
            });
        }

        CorrectionPayload? correction = request.Action == "corrected"
            ? new CorrectionPayload
              {
                  NormalizedValue = request.CorrectedNormalizedValue,
                  RawText         = request.CorrectedRawText,
                  Unit            = request.CorrectedUnit,
              }
            : null;

        var (row, remainingCounts) = await _verificationService.VerifySingleAsync(
            id, request.Action, verifierId, verifierName, correction, cancellationToken);

        if (row is null)
        {
            return NotFound(new ErrorResponse
            {
                StatusCode = 404,
                Message    = $"Extracted data row {id} not found or not eligible for verification.",
            });
        }

        await _auditLog.LogAsync(
            AuditAction.ExtractedDataVerified,
            verifierId,
            resourceType:       "ExtractedData",
            ipAddress:          GetClientIp(),
            userAgent:          GetUserAgent(),
            resourceId:         id,
            cancellationToken:  cancellationToken);

        return Ok(new ExtractedDataVerificationResponse
        {
            VerifiedCount         = 1,
            SkippedCount          = 0,
            UpdatedRows           = [new VerifiedRowSummary
            {
                ExtractedDataId    = row.ExtractedDataId,
                VerificationStatus = row.VerificationStatus,
                VerifiedAtUtc      = row.VerifiedAtUtc,
                VerifiedByName     = row.VerifiedByName,
            }],
            RemainingFlaggedCounts = remainingCounts,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/extracted-data/bulk-verify
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Bulk-verifies multiple flagged extracted data rows in one operation (US_041 EC-2).
    ///
    /// Already-verified rows are silently skipped (idempotent).
    /// Returns per-row updated state and refreshed remaining flagged counts for each
    /// affected document.
    /// </summary>
    [HttpPost("bulk-verify")]
    [ProducesResponseType(typeof(ExtractedDataVerificationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> BulkVerify(
        [FromBody] BulkVerifyExtractedDataRequest request,
        CancellationToken                         cancellationToken)
    {
        var (verifierId, verifierName) = await ResolveVerifierAsync();
        if (verifierId == Guid.Empty) return Unauthorized();

        var (verifiedRows, skipped, remainingCounts) = await _verificationService.BulkVerifyAsync(
            request.ExtractedDataIds, verifierId, verifierName, cancellationToken);

        await _auditLog.LogAsync(
            AuditAction.ExtractedDataBulkVerified,
            verifierId,
            resourceType:      "ExtractedData",
            ipAddress:         GetClientIp(),
            userAgent:         GetUserAgent(),
            cancellationToken: cancellationToken);

        return Ok(new ExtractedDataVerificationResponse
        {
            VerifiedCount          = verifiedRows.Count,
            SkippedCount           = skipped,
            UpdatedRows            = verifiedRows.Select(r => new VerifiedRowSummary
            {
                ExtractedDataId    = r.ExtractedDataId,
                VerificationStatus = r.VerificationStatus,
                VerifiedAtUtc      = r.VerifiedAtUtc,
                VerifiedByName     = r.VerifiedByName,
            }).ToList(),
            RemainingFlaggedCounts = remainingCounts,
        });
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private async Task<(Guid Id, string Name)> ResolveVerifierAsync()
    {
        var idValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(idValue) || !Guid.TryParse(idValue, out var userId))
            return (Guid.Empty, string.Empty);

        var user = await _userManager.FindByIdAsync(idValue);
        return (userId, user?.FullName ?? idValue);
    }

    private string GetClientIp() =>
        HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private string GetUserAgent() =>
        Request.Headers.UserAgent.ToString();

    private static ExtractedDataRow MapToResponse(ExtractedDataQueryRow row)
    {
        ExtractedDataContentDto? content = row.DataContent is null
            ? null
            : new ExtractedDataContentDto
            {
                RawText         = row.DataContent.RawText,
                NormalizedValue = row.DataContent.NormalizedValue,
                Unit            = row.DataContent.Unit,
                SourceSnippet   = row.DataContent.SourceSnippet,
                Metadata        = row.DataContent.Metadata,
            };

        return new ExtractedDataRow
        {
            ExtractedDataId    = row.ExtractedDataId,
            DocumentId         = row.DocumentId,
            DataType           = row.DataType,
            DataContent        = content,
            ConfidenceScore    = row.ConfidenceScore,
            FlaggedForReview   = row.FlaggedForReview,
            ReviewReason       = row.ReviewReason,
            VerificationStatus = row.VerificationStatus,
            VerifiedAtUtc      = row.VerifiedAtUtc,
            VerifiedByName     = row.VerifiedByName,
            PageNumber         = row.PageNumber,
            ExtractionRegion   = row.ExtractionRegion,
        };
    }
}
