using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UPACIP.Api.Authorization;
using UPACIP.Api.Middleware;
using UPACIP.Api.Models;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Coding;

namespace UPACIP.Api.Controllers;

/// <summary>
/// REST endpoints for the human-in-the-loop code verification workflow (US_049, AC-1 through AC-4,
/// EC-1, EC-2, FR-064, AIR-009).
///
/// Routes:
///   GET  /api/patients/{patientId}/codes/verification-queue  — Pending AI codes for staff review (AC-1).
///   PUT  /api/codes/{codeId}/approve                         — Approve an AI-suggested code (AC-2, EC-1).
///   PUT  /api/codes/{codeId}/override                        — Override with a replacement and justification (AC-3).
///   GET  /api/codes/{codeId}/audit-trail                     — Immutable audit history for a code (AC-4).
///   GET  /api/codes/search                                   — Code-library search for the override modal (AC-3).
///   GET  /api/patients/{patientId}/codes/verification-progress — Progress counts + status label (EC-2).
///   GET  /api/codes/{codeId}/deprecation-check               — Deprecated-code check with replacements (EC-1).
///
/// Authorization (OWASP A01 — Broken Access Control):
///   All endpoints are restricted to the <c>Staff</c> or <c>Admin</c> role.
///   The acting user's ID is always read from the authenticated JWT claims; it is never
///   accepted from the request body to prevent privilege escalation (OWASP A01).
///
/// Business exception mapping:
///   <see cref="DeprecatedCodeException"/> → 409 Conflict with <see cref="DeprecatedCodeConflictDto"/>.
///   <see cref="KeyNotFoundException"/>    → 404 Not Found.
///   <see cref="ArgumentException"/>       → 400 Bad Request (validation failures).
/// </summary>
[ApiController]
[Authorize(Policy = RbacPolicies.StaffOrAdmin)]
[Produces("application/json")]
public sealed class CodeVerificationController : ControllerBase
{
    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ICodeVerificationService            _verificationService;
    private readonly ILogger<CodeVerificationController> _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public CodeVerificationController(
        ICodeVerificationService            verificationService,
        ILogger<CodeVerificationController> logger)
    {
        _verificationService = verificationService;
        _logger              = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/patients/{patientId}/codes/verification-queue
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all pending AI-generated codes for the patient, ready for staff review (US_049 AC-1).
    ///
    /// Each item includes the code value, description, AI justification, confidence score,
    /// and a deprecated flag so the UI can disable the "Approve" button for deprecated codes (EC-1).
    /// </summary>
    /// <param name="patientId">Target patient primary key.</param>
    /// <param name="codeType">
    /// Optional filter: <c>icd10</c> or <c>cpt</c>.
    /// When omitted both code types are returned.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("api/patients/{patientId:guid}/codes/verification-queue")]
    [ProducesResponseType(typeof(IReadOnlyList<VerificationQueueDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetVerificationQueueAsync(
        [FromRoute] Guid    patientId,
        [FromQuery] string? codeType = null,
        CancellationToken   ct       = default)
    {
        var items = await _verificationService.GetVerificationQueueAsync(patientId, ct);

        // Apply optional code-type filter.
        if (!string.IsNullOrWhiteSpace(codeType))
        {
            if (!TryParseCodeType(codeType, out var parsedType))
            {
                return BadRequest(BuildError(400, $"Invalid code_type '{codeType}'. Valid values: icd10, cpt."));
            }

            items = items.Where(i => i.CodeType == parsedType).ToList();
        }

        var dtos = items.Select(i => new VerificationQueueDto
        {
            CodeId           = i.CodeId,
            CodeType         = i.CodeType.ToString().ToLowerInvariant(),
            CodeValue        = i.CodeValue,
            Description      = i.Description,
            Justification    = i.Justification,
            AiConfidenceScore = i.AiConfidenceScore,
            Status           = i.VerificationStatus.ToString().ToLowerInvariant(),
            SuggestedByAi    = true, // queue only contains AI-suggested codes
            IsDeprecated     = i.IsDeprecated,
            CreatedAt        = i.CreatedAt,
        }).ToList();

        return Ok(dtos);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/codes/{codeId}/approve
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Approves an AI-suggested medical code (US_049, AC-2).
    ///
    /// Sets the code status to <c>Verified</c>, records staff attribution and timestamp,
    /// and writes an immutable <c>CodingAuditLog</c> entry with action <c>Approved</c> (AC-4).
    ///
    /// Returns 409 Conflict when the code is deprecated in the current library (EC-1),
    /// with a <see cref="DeprecatedCodeConflictDto"/> payload carrying replacement code suggestions.
    /// </summary>
    /// <param name="codeId">Primary key of the <c>MedicalCode</c> to approve.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPut("api/codes/{codeId:guid}/approve")]
    [ProducesResponseType(typeof(ApproveCodeResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(DeprecatedCodeConflictDto), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ApproveCodeAsync(
        [FromRoute] Guid   codeId,
        CancellationToken  ct = default)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var correlationId = GetCorrelationId();

        try
        {
            var code = await _verificationService.ApproveCodeAsync(codeId, userId.Value, correlationId, ct);

            return Ok(new ApproveCodeResponseDto
            {
                CodeId     = code.Id,
                Status     = "verified",
                VerifiedAt = code.VerifiedAt,
            });
        }
        catch (DeprecatedCodeException ex)
        {
            _logger.LogWarning(
                "CodeVerificationController.ApproveCodeAsync: deprecated code blocked. " +
                "CodeId={CodeId} UserId={UserId} CorrelationId={CorrelationId}",
                codeId, userId, correlationId);

            return Conflict(new DeprecatedCodeConflictDto
            {
                Message          = ex.Message,
                DeprecatedNotice = ex.DeprecationResult.NoticeText,
                ReplacementCodes = ex.DeprecationResult.ReplacementCodes,
                CorrelationId    = correlationId,
            });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(BuildError(404, ex.Message));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/codes/{codeId}/override
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Overrides an AI-suggested code with a staff-selected replacement value (US_049, AC-3, AC-4).
    ///
    /// Stores the original code value in <c>original_code_value</c>, applies the replacement,
    /// records the mandatory justification, and writes an immutable <c>CodingAuditLog</c> entry
    /// with action <c>Overridden</c> and both old and new code values (AC-4).
    ///
    /// Returns 400 Bad Request when justification is shorter than 10 characters or the
    /// replacement code format is invalid.
    /// </summary>
    /// <param name="codeId">Primary key of the <c>MedicalCode</c> to override.</param>
    /// <param name="request">Replacement code value, description, and justification.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPut("api/codes/{codeId:guid}/override")]
    [ProducesResponseType(typeof(OverrideCodeResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> OverrideCodeAsync(
        [FromRoute] Guid                codeId,
        [FromBody]  OverrideCodeRequestDto request,
        CancellationToken               ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(BuildValidationError());

        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized();

        var correlationId = GetCorrelationId();

        try
        {
            var code = await _verificationService.OverrideCodeAsync(
                codeId,
                userId.Value,
                request.NewCodeValue,
                request.NewDescription,
                request.Justification,
                correlationId,
                ct);

            return Ok(new OverrideCodeResponseDto
            {
                CodeId            = code.Id,
                Status            = "overridden",
                OriginalCodeValue = code.OriginalCodeValue,
                NewCodeValue      = code.CodeValue,
                Justification     = code.OverrideJustification ?? request.Justification,
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(BuildError(400, ex.Message));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(BuildError(404, ex.Message));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/codes/{codeId}/audit-trail
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the immutable audit trail for a specific medical code ordered by
    /// timestamp descending (US_049, AC-4).
    /// Each entry records who did what, when, and what changed.
    /// </summary>
    /// <param name="codeId">Primary key of the <c>MedicalCode</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("api/codes/{codeId:guid}/audit-trail")]
    [ProducesResponseType(typeof(IReadOnlyList<CodingAuditEntryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAuditTrailAsync(
        [FromRoute] Guid  codeId,
        CancellationToken ct = default)
    {
        var entries = await _verificationService.GetAuditTrailAsync(codeId, ct);

        var dtos = entries.Select(e => new CodingAuditEntryDto
        {
            LogId        = e.LogId,
            Action       = e.Action.ToString(),
            OldCodeValue = e.OldCodeValue,
            NewCodeValue = e.NewCodeValue,
            Justification = e.Justification,
            UserId       = e.UserId,
            Timestamp    = e.Timestamp,
        }).ToList();

        return Ok(dtos);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/codes/search
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Searches the active ICD-10 or CPT code library by code value or description using a
    /// case-insensitive contains match (US_049, AC-3).
    /// Used by the override modal to let staff find the correct replacement code.
    /// </summary>
    /// <param name="query">Search term (code value prefix or description fragment).</param>
    /// <param name="type">Code type: <c>icd10</c> or <c>cpt</c>. Required.</param>
    /// <param name="limit">Maximum number of results to return (default 20, max 50).</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("api/codes/search")]
    [ProducesResponseType(typeof(IReadOnlyList<CodeSearchResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SearchCodesAsync(
        [FromQuery] string  query,
        [FromQuery] string  type,
        [FromQuery] int     limit = 20,
        CancellationToken   ct    = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest(BuildError(400, "query parameter is required."));

        if (!TryParseCodeType(type, out var codeType))
            return BadRequest(BuildError(400, $"Invalid type '{type}'. Valid values: icd10, cpt."));

        var results = await _verificationService.SearchCodesAsync(query, codeType, limit, ct);

        var dtos = results.Select(r => new CodeSearchResultDto
        {
            CodeValue   = r.CodeValue,
            Description = r.Description,
            Category    = r.Category,
        }).ToList();

        return Ok(dtos);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/patients/{patientId}/codes/verification-progress
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns total, verified, overridden, and pending code counts for the patient with a
    /// derived status label (US_049, EC-2).
    /// Drives the progress indicator on the SCR-014 screen (e.g., "3/5 codes verified").
    /// </summary>
    /// <param name="patientId">Target patient primary key.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("api/patients/{patientId:guid}/codes/verification-progress")]
    [ProducesResponseType(typeof(VerificationProgressDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetVerificationProgressAsync(
        [FromRoute] Guid  patientId,
        CancellationToken ct = default)
    {
        var progress = await _verificationService.GetVerificationProgressAsync(patientId, ct);

        return Ok(new VerificationProgressDto
        {
            TotalCodes      = progress.Total,
            VerifiedCount   = progress.Verified,
            OverriddenCount = progress.Overridden,
            PendingCount    = progress.Pending,
            DeprecatedCount = progress.Deprecated,
            StatusLabel     = progress.StatusLabel,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/codes/{codeId}/deprecation-check
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Checks whether the specified code is deprecated in the current library and returns
    /// replacement suggestions when available (US_049, EC-1).
    /// The UI can call this endpoint when rendering the code details panel to pre-warn
    /// staff before they attempt an approval.
    /// </summary>
    /// <param name="codeId">Primary key of the <c>MedicalCode</c> to check.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("api/codes/{codeId:guid}/deprecation-check")]
    [ProducesResponseType(typeof(DeprecationCheckDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CheckDeprecationAsync(
        [FromRoute] Guid  codeId,
        CancellationToken ct = default)
    {
        // Delegates to CheckDeprecatedByCodeIdAsync which resolves CodeValue + CodeType from
        // the MedicalCode row, then checks the code library for deprecation status (EC-1).
        var result = await _verificationService.CheckDeprecatedByCodeIdAsync(codeId, ct);

        if (result is null)
            return NotFound(BuildError(404, $"MedicalCode '{codeId}' not found."));

        return Ok(new DeprecationCheckDto
        {
            IsDeprecated     = result.IsDeprecated,
            DeprecatedNotice = result.NoticeText,
            ReplacementCodes = result.ReplacementCodes,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private string GetCorrelationId()
        => HttpContext.Items[CorrelationIdMiddleware.ItemsKey]?.ToString()
           ?? Guid.NewGuid().ToString();

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
               ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private ErrorResponse BuildError(int statusCode, string message) => new()
    {
        StatusCode    = statusCode,
        Message       = message,
        CorrelationId = GetCorrelationId(),
        Timestamp     = DateTimeOffset.UtcNow,
    };

    private ErrorResponse BuildValidationError() => new()
    {
        StatusCode       = 400,
        Message          = "One or more validation errors occurred.",
        CorrelationId    = GetCorrelationId(),
        Timestamp        = DateTimeOffset.UtcNow,
        ValidationErrors = ModelState.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value?.Errors.Select(e => e.ErrorMessage).ToArray() ?? []),
    };

    private static bool TryParseCodeType(string? value, out CodeType codeType)
    {
        codeType = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Trim().ToLowerInvariant() switch
        {
            "icd10" => (codeType = CodeType.Icd10) == CodeType.Icd10,
            "cpt"   => (codeType = CodeType.Cpt)   == CodeType.Cpt,
            _       => false,
        };
    }
}
