using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UPACIP.Api.Authorization;
using UPACIP.Api.Middleware;
using UPACIP.Api.Models;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Conflict;
using ServiceConflictResolutionRequest = UPACIP.Service.Conflict.ConflictResolutionRequest;

namespace UPACIP.Api.Controllers;

/// <summary>
/// RESTful endpoints for the clinical conflict lifecycle (US_044, AC-2, AC-3, FR-053)
/// and the staff resolution workflow (US_045, AC-2, AC-4, EC-1, EC-2, FR-054).
///
/// Patient-scoped routes:
///   GET    /api/patients/{patientId}/conflicts                                    — Filtered, paged list of conflicts for a patient.
///   GET    /api/patients/{patientId}/conflicts/summary                            — Count aggregation by type and severity.
///   GET    /api/patients/{patientId}/conflicts/resolution-progress                — Resolved/total counts and verification status (US_045 EC-1).
///   GET    /api/patients/{patientId}/conflicts/{conflictId}                       — Full detail with AI explanation and source citations.
///   PUT    /api/patients/{patientId}/conflicts/{conflictId}/resolve               — Resolve a conflict with staff notes and attribution.
///   PUT    /api/patients/{patientId}/conflicts/{conflictId}/dismiss               — Dismiss a false-positive conflict.
///   PUT    /api/patients/{patientId}/conflicts/{conflictId}/select-value          — Select the correct data value from conflicting sources (US_045 AC-2).
///   PUT    /api/patients/{patientId}/conflicts/{conflictId}/both-valid            — Mark both values valid with different date attribution (US_045 EC-2).
///   GET    /api/patients/{patientId}/profile/verification-status                  — Profile verification lifecycle state (US_045 AC-4).
///
/// Global staff route:
///   GET    /api/conflicts/review-queue                                            — Cross-patient urgent-first review queue with pagination.
///
/// Authorization: Staff or Admin role only (FR-002).
///
/// OWASP A01 — Broken Access Control:
///   The <c>conflictId</c> route parameter is always joined to <c>patientId</c> in the
///   database query — ownership is validated server-side and never trusted from the URL alone.
///   The current user ID for staff attribution is read from the JWT, never from request body.
/// </summary>
[ApiController]
[Authorize(Policy = RbacPolicies.StaffOrAdmin)]
[Produces("application/json")]
public sealed class ConflictController : ControllerBase
{
    private readonly ApplicationDbContext            _db;
    private readonly IConflictManagementService      _conflictService;
    private readonly IConflictResolutionService      _resolutionService;
    private readonly ILogger<ConflictController>     _logger;

    public ConflictController(
        ApplicationDbContext          db,
        IConflictManagementService    conflictService,
        IConflictResolutionService    resolutionService,
        ILogger<ConflictController>   logger)
    {
        _db                = db;
        _conflictService   = conflictService;
        _resolutionService = resolutionService;
        _logger            = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/patients/{patientId}/conflicts
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a filtered, paged list of clinical conflicts for a patient (US_044, AC-2, FR-053).
    ///
    /// Supports optional filtering by status, severity, and type via query string parameters.
    /// Results are ordered: urgent conflicts first, then by detection date descending.
    /// </summary>
    /// <param name="patientId">Target patient primary key.</param>
    /// <param name="query">Optional filter and pagination parameters.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Paged conflict list with total count for client-side pagination controls.</returns>
    [HttpGet("api/patients/{patientId:guid}/conflicts")]
    [ProducesResponseType(typeof(ConflictPagedResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetConflictsAsync(
        Guid patientId,
        [FromQuery] ConflictQueryParameters query,
        CancellationToken ct)
    {
        if (!await PatientExistsAsync(patientId, ct))
            return NotFound(BuildError(404, $"Patient {patientId} not found."));

        var page     = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 100);

        var q = _db.ClinicalConflicts
            .AsNoTracking()
            .Where(c => c.PatientId == patientId)
            .Include(c => c.Patient)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Status) &&
            Enum.TryParse<ConflictStatus>(query.Status, ignoreCase: true, out var statusFilter))
            q = q.Where(c => c.Status == statusFilter);

        if (!string.IsNullOrWhiteSpace(query.Severity) &&
            Enum.TryParse<ConflictSeverity>(query.Severity, ignoreCase: true, out var severityFilter))
            q = q.Where(c => c.Severity == severityFilter);

        if (!string.IsNullOrWhiteSpace(query.Type) &&
            Enum.TryParse<ConflictType>(query.Type, ignoreCase: true, out var typeFilter))
            q = q.Where(c => c.ConflictType == typeFilter);

        var totalCount = await q.CountAsync(ct);

        // Load paged results client-side; JSONB list .Count cannot be translated to SQL.
        var entities = await q
            .OrderByDescending(c => c.IsUrgent)
            .ThenByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = entities.Select(c => new ConflictListDto
        {
            ConflictId          = c.Id,
            ConflictType        = c.ConflictType,
            Severity            = c.Severity,
            Status              = c.Status,
            IsUrgent            = c.IsUrgent,
            PatientName         = c.Patient.FullName,
            ConflictDescription = c.ConflictDescription,
            SourceDocumentCount = c.SourceDocumentIds.Count,
            AiConfidenceScore   = c.AiConfidenceScore,
            CreatedAt           = c.CreatedAt,
        }).ToList();

        return Ok(new ConflictPagedResult
        {
            Items      = items,
            TotalCount = totalCount,
            Page       = page,
            PageSize   = pageSize,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/patients/{patientId}/conflicts/summary
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns conflict count aggregations by type and severity for a patient (US_044, FR-053).
    ///
    /// Only counts open conflicts: Detected and UnderReview statuses.
    /// Used by the staff dashboard to render conflict-count badges without loading full lists.
    /// </summary>
    /// <param name="patientId">Target patient primary key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Conflict count summary broken down by type and severity.</returns>
    [HttpGet("api/patients/{patientId:guid}/conflicts/summary")]
    [ProducesResponseType(typeof(ConflictSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetConflictSummaryAsync(
        Guid patientId,
        CancellationToken ct)
    {
        if (!await PatientExistsAsync(patientId, ct))
            return NotFound(BuildError(404, $"Patient {patientId} not found."));

        var openStatuses = new[] { ConflictStatus.Detected, ConflictStatus.UnderReview };

        var open = await _db.ClinicalConflicts
            .AsNoTracking()
            .Where(c => c.PatientId == patientId && openStatuses.Contains(c.Status))
            .Select(c => new { c.IsUrgent, c.ConflictType, c.Severity })
            .ToListAsync(ct);

        return Ok(new ConflictSummaryDto
        {
            TotalOpen   = open.Count,
            TotalUrgent = open.Count(c => c.IsUrgent),
            ByType      = open.GroupBy(c => c.ConflictType.ToString())
                              .ToDictionary(g => g.Key, g => g.Count()),
            BySeverity  = open.GroupBy(c => c.Severity.ToString())
                              .ToDictionary(g => g.Key, g => g.Count()),
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/patients/{patientId}/conflicts/{conflictId}
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the full conflict detail, including AI explanation and source document citations
    /// for every involved extracted data point (US_044, AC-2, AC-3, AIR-007, FR-053).
    ///
    /// Source citations are loaded by joining the conflict's SourceExtractedDataIds list to
    /// the ExtractedData table, with their parent ClinicalDocument included for provenance.
    /// </summary>
    /// <param name="patientId">Target patient primary key.</param>
    /// <param name="conflictId">ClinicalConflict primary key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Full conflict detail with source citation chain.</returns>
    [HttpGet("api/patients/{patientId:guid}/conflicts/{conflictId:guid}")]
    [ProducesResponseType(typeof(ConflictDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetConflictDetailAsync(
        Guid patientId,
        Guid conflictId,
        CancellationToken ct)
    {
        // OWASP A01: join conflictId to patientId — never trust URL params alone.
        var conflict = await _db.ClinicalConflicts
            .AsNoTracking()
            .Include(c => c.Patient)
            .FirstOrDefaultAsync(c => c.Id == conflictId && c.PatientId == patientId, ct);

        if (conflict is null)
            return NotFound(BuildError(404, $"Conflict {conflictId} not found for patient {patientId}."));

        // Load source citations via extracted data IDs stored in JSONB column.
        var extractedDataIds = conflict.SourceExtractedDataIds;
        var extractedRows = extractedDataIds.Count > 0
            ? await _db.ExtractedData
                .AsNoTracking()
                .Include(e => e.Document)
                .Where(e => extractedDataIds.Contains(e.Id))
                .ToListAsync(ct)
            : [];

        var citations = extractedRows.Select(e => new ConflictSourceCitationDto
        {
            DocumentId            = e.DocumentId,
            DocumentName          = e.Document.OriginalFileName,
            DocumentCategory      = e.Document.DocumentCategory.ToString(),
            UploadDate            = e.Document.UploadDate,
            ExtractedDataId       = e.Id,
            DataType              = e.DataType.ToString(),
            NormalizedValue       = e.DataContent?.NormalizedValue,
            RawText               = e.DataContent?.RawText,
            Unit                  = e.DataContent?.Unit,
            SourceSnippet         = e.DataContent?.SourceSnippet,
            ConfidenceScore       = e.ConfidenceScore,
            SourceAttributionText = e.SourceAttribution ?? string.Empty,
            PageNumber            = e.PageNumber,
            ExtractionRegion      = e.ExtractionRegion ?? string.Empty,
        }).ToList();

        string? resolvedByUserName = null;
        if (conflict.ResolvedByUserId.HasValue)
        {
            resolvedByUserName = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == conflict.ResolvedByUserId.Value)
                .Select(u => u.UserName)
                .FirstOrDefaultAsync(ct);
        }

        return Ok(new ConflictDetailDto
        {
            ConflictId          = conflict.Id,
            PatientId           = conflict.PatientId,
            PatientName         = conflict.Patient.FullName,
            ConflictType        = conflict.ConflictType,
            Severity            = conflict.Severity,
            Status              = conflict.Status,
            IsUrgent            = conflict.IsUrgent,
            ConflictDescription = conflict.ConflictDescription,
            AiExplanation       = conflict.AiExplanation,
            AiConfidenceScore   = conflict.AiConfidenceScore,
            SourceCitations     = citations,
            CreatedAt           = conflict.CreatedAt,
            ResolvedByUserName  = resolvedByUserName,
            ResolutionNotes     = conflict.ResolutionNotes,
            ResolvedAt          = conflict.ResolvedAt,
            ProfileVersionId    = conflict.ProfileVersionId,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/patients/{patientId}/conflicts/{conflictId}/resolve
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves an open conflict, recording the resolving staff member and their clinical notes
    /// (US_044, AC-2, FR-053).
    ///
    /// Staff identity is read from the authenticated JWT — it is never accepted from the request body.
    /// Returns 204 No Content on success.
    /// </summary>
    /// <param name="patientId">Target patient primary key.</param>
    /// <param name="conflictId">ClinicalConflict primary key.</param>
    /// <param name="dto">Resolution request body containing required resolution notes.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPut("api/patients/{patientId:guid}/conflicts/{conflictId:guid}/resolve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResolveConflictAsync(
        Guid patientId,
        Guid conflictId,
        [FromBody] ResolveConflictDto dto,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.ResolutionNotes))
            return BadRequest(BuildError(400, "ResolutionNotes is required to resolve a conflict."));

        // OWASP A01: validate ownership before acting.
        var exists = await _db.ClinicalConflicts
            .AsNoTracking()
            .AnyAsync(c => c.Id == conflictId && c.PatientId == patientId, ct);
        if (!exists)
            return NotFound(BuildError(404, $"Conflict {conflictId} not found for patient {patientId}."));

        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized(BuildError(401, "Unable to determine authenticated user identity."));

        try
        {
            await _conflictService.ResolveConflictAsync(
                new ServiceConflictResolutionRequest
                {
                    ConflictId       = conflictId,
                    ResolvedByUserId = userId.Value,
                    ResolutionNotes  = dto.ResolutionNotes,
                },
                ct);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex,
                "Conflict {ConflictId} could not be resolved for patient {PatientId}: {Reason}",
                conflictId, patientId, ex.Message);
            return Conflict(BuildError(409, ex.Message));
        }

        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/patients/{patientId}/conflicts/{conflictId}/dismiss
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Dismisses an open conflict as a false positive, recording the staff member and their notes
    /// (US_044, FR-053).
    ///
    /// Staff identity is read from the authenticated JWT — it is never accepted from the request body.
    /// Returns 204 No Content on success.
    /// </summary>
    /// <param name="patientId">Target patient primary key.</param>
    /// <param name="conflictId">ClinicalConflict primary key.</param>
    /// <param name="dto">Dismissal request body containing required dismissal notes.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPut("api/patients/{patientId:guid}/conflicts/{conflictId:guid}/dismiss")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DismissConflictAsync(
        Guid patientId,
        Guid conflictId,
        [FromBody] ResolveConflictDto dto,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.ResolutionNotes))
            return BadRequest(BuildError(400, "ResolutionNotes is required to dismiss a conflict."));

        // OWASP A01: validate ownership before acting.
        var exists = await _db.ClinicalConflicts
            .AsNoTracking()
            .AnyAsync(c => c.Id == conflictId && c.PatientId == patientId, ct);
        if (!exists)
            return NotFound(BuildError(404, $"Conflict {conflictId} not found for patient {patientId}."));

        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized(BuildError(401, "Unable to determine authenticated user identity."));

        try
        {
            await _conflictService.DismissConflictAsync(
                new ServiceConflictResolutionRequest
                {
                    ConflictId       = conflictId,
                    ResolvedByUserId = userId.Value,
                    ResolutionNotes  = dto.ResolutionNotes,
                },
                ct);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex,
                "Conflict {ConflictId} could not be dismissed for patient {PatientId}: {Reason}",
                conflictId, patientId, ex.Message);
            return Conflict(BuildError(409, ex.Message));
        }

        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/conflicts/review-queue
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the global cross-patient staff review queue (US_044, AC-3, FR-053).
    ///
    /// Conflicts are ordered: urgent first, then by detection date descending.
    /// Supports pagination via <paramref name="page"/> and <paramref name="pageSize"/> query params.
    /// </summary>
    /// <param name="page">1-based page number (default: 1).</param>
    /// <param name="pageSize">Items per page (default: 20, max: 100).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Paged review queue with total count.</returns>
    [HttpGet("/api/conflicts/review-queue")]
    [ProducesResponseType(typeof(ReviewQueuePage), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetReviewQueueAsync(
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct     = default)
    {
        page     = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var result = await _conflictService.GetReviewQueueAsync(page, pageSize, ct);
        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/patients/{patientId}/conflicts/resolution-progress  (US_045 EC-1, AC-4)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns conflict resolution progress for a patient (US_045, EC-1, AC-4).
    ///
    /// Provides total/resolved/remaining counts and current verification status so staff
    /// can resume partial resolution work after navigating away.
    /// </summary>
    /// <param name="patientId">Target patient primary key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Progress snapshot with counts, percentage, and verification status.</returns>
    [HttpGet("api/patients/{patientId:guid}/conflicts/resolution-progress")]
    [ProducesResponseType(typeof(ResolutionProgressResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetResolutionProgressAsync(
        Guid patientId,
        CancellationToken ct)
    {
        if (!await PatientExistsAsync(patientId, ct))
            return NotFound(BuildError(404, $"Patient {patientId} not found."));

        var progress = await _resolutionService.GetResolutionProgressAsync(patientId, ct);

        return Ok(new ResolutionProgressResponseDto
        {
            PatientId          = progress.PatientId,
            TotalConflicts     = progress.TotalConflicts,
            ResolvedCount      = progress.ResolvedCount,
            RemainingCount     = progress.RemainingCount,
            PercentComplete    = progress.PercentComplete,
            VerificationStatus = progress.VerificationStatus,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/patients/{patientId}/conflicts/{conflictId}/select-value  (US_045 AC-2)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a conflict by selecting the correct data value from the conflicting sources (US_045, AC-2).
    ///
    /// The chosen <c>ExtractedData</c> value is written to the consolidated profile and the conflict
    /// is marked Resolved with SelectedValue resolution type and staff attribution.
    /// Staff identity is read from the authenticated JWT.
    /// </summary>
    /// <param name="patientId">Target patient primary key.</param>
    /// <param name="conflictId">ClinicalConflict primary key.</param>
    /// <param name="dto">Selection request: chosen extracted data ID and resolution notes.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 No Content on success.</returns>
    [HttpPut("api/patients/{patientId:guid}/conflicts/{conflictId:guid}/select-value")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> SelectConflictValueAsync(
        Guid patientId,
        Guid conflictId,
        [FromBody] SelectValueRequestDto dto,
        CancellationToken ct)
    {
        // OWASP A01: validate ownership before acting.
        var exists = await _db.ClinicalConflicts
            .AsNoTracking()
            .AnyAsync(c => c.Id == conflictId && c.PatientId == patientId, ct);
        if (!exists)
            return NotFound(BuildError(404, $"Conflict {conflictId} not found for patient {patientId}."));

        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized(BuildError(401, "Unable to determine authenticated user identity."));

        try
        {
            await _resolutionService.SelectConflictValueAsync(
                new UPACIP.Service.Conflict.SelectValueRequest
                {
                    ConflictId               = conflictId,
                    SelectedExtractedDataId  = dto.SelectedExtractedDataId,
                    UserId                   = userId.Value,
                    ResolutionNotes          = dto.ResolutionNotes,
                },
                ct);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not a source"))
        {
            _logger.LogWarning(
                "Conflict {ConflictId}: SelectedExtractedDataId {DataId} not in conflict sources.",
                conflictId, dto.SelectedExtractedDataId);
            return UnprocessableEntity(BuildError(422, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex,
                "Conflict {ConflictId} could not be resolved for patient {PatientId}: {Reason}",
                conflictId, patientId, ex.Message);
            return Conflict(BuildError(409, ex.Message));
        }

        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/patients/{patientId}/conflicts/{conflictId}/both-valid  (US_045 EC-2)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a conflict by confirming both values are valid with different date attribution
    /// (US_045, EC-2 — "Both Valid — Different Dates").
    ///
    /// Both source data entries are preserved in the consolidated profile.
    /// Staff identity is read from the authenticated JWT.
    /// </summary>
    /// <param name="patientId">Target patient primary key.</param>
    /// <param name="conflictId">ClinicalConflict primary key.</param>
    /// <param name="dto">BothValid request: clinical explanation (required, ≥10 chars).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 No Content on success.</returns>
    [HttpPut("api/patients/{patientId:guid}/conflicts/{conflictId:guid}/both-valid")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResolveBothValidAsync(
        Guid patientId,
        Guid conflictId,
        [FromBody] BothValidRequestDto dto,
        CancellationToken ct)
    {
        // OWASP A01: validate ownership before acting.
        var exists = await _db.ClinicalConflicts
            .AsNoTracking()
            .AnyAsync(c => c.Id == conflictId && c.PatientId == patientId, ct);
        if (!exists)
            return NotFound(BuildError(404, $"Conflict {conflictId} not found for patient {patientId}."));

        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized(BuildError(401, "Unable to determine authenticated user identity."));

        try
        {
            await _resolutionService.ResolveBothValidAsync(
                new UPACIP.Service.Conflict.BothValidRequest
                {
                    ConflictId  = conflictId,
                    UserId      = userId.Value,
                    Explanation = dto.Explanation,
                },
                ct);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex,
                "Conflict {ConflictId} BothValid resolution failed for patient {PatientId}: {Reason}",
                conflictId, patientId, ex.Message);
            return Conflict(BuildError(409, ex.Message));
        }

        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/patients/{patientId}/profile/verification-status  (US_045 AC-4)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the verification lifecycle state of the patient's latest profile version (US_045, AC-4).
    ///
    /// Shows whether all conflicts have been resolved (Verified), some remain (PartiallyVerified),
    /// or no resolution has started (Unverified).  Includes staff attribution when Verified.
    /// </summary>
    /// <param name="patientId">Target patient primary key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Verification status with optional staff attribution and timestamp.</returns>
    [HttpGet("api/patients/{patientId:guid}/profile/verification-status")]
    [ProducesResponseType(typeof(ProfileVerificationResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfileVerificationStatusAsync(
        Guid patientId,
        CancellationToken ct)
    {
        if (!await PatientExistsAsync(patientId, ct))
            return NotFound(BuildError(404, $"Patient {patientId} not found."));

        var latestVersion = await _db.PatientProfileVersions
            .AsNoTracking()
            .Where(v => v.PatientId == patientId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new
            {
                v.VerificationStatus,
                v.VerifiedByUserId,
                v.VerifiedAt,
            })
            .FirstOrDefaultAsync(ct);

        if (latestVersion is null)
            return NotFound(BuildError(404, $"No profile version found for patient {patientId}."));

        string? verifiedByUserName = null;
        if (latestVersion.VerifiedByUserId.HasValue)
        {
            verifiedByUserName = await _db.Users
                .AsNoTracking()
                .Where(u => u.Id == latestVersion.VerifiedByUserId.Value)
                .Select(u => u.UserName)
                .FirstOrDefaultAsync(ct);
        }

        return Ok(new ProfileVerificationResponseDto
        {
            Status             = latestVersion.VerificationStatus.ToString(),
            VerifiedByUserId   = latestVersion.VerifiedByUserId,
            VerifiedByUserName = verifiedByUserName,
            VerifiedAt         = latestVersion.VerifiedAt,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<bool> PatientExistsAsync(Guid patientId, CancellationToken ct)
        => await _db.Patients
            .AsNoTracking()
            .AnyAsync(p => p.Id == patientId && p.DeletedAt == null, ct);

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
               ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private ErrorResponse BuildError(int statusCode, string message)
    {
        var correlationId = HttpContext.Items[CorrelationIdMiddleware.ItemsKey]?.ToString()
                            ?? Guid.NewGuid().ToString();
        return new ErrorResponse
        {
            StatusCode    = statusCode,
            Message       = message,
            CorrelationId = correlationId,
            Timestamp     = DateTimeOffset.UtcNow,
        };
    }
}
