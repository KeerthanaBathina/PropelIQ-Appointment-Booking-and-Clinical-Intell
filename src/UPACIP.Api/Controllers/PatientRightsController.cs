using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPACIP.Api.Authorization;
using UPACIP.Api.Models;
using UPACIP.Service.PatientRights;
using UPACIP.Service.PatientRights.Deletion;
using Asp.Versioning;

namespace UPACIP.Api.Controllers;

/// <summary>
/// HIPAA Right of Access and Right to Deletion endpoints (US_094, NFR-044, NFR-045).
///
/// Patient endpoints:
///   POST /api/patients/{patientId}/data-access-request             — Submit access request.
///   GET  /api/patients/{patientId}/data-access-request/{requestId} — Check request status.
///   GET  /api/patients/{patientId}/data-access-request/{requestId}/download — Download ZIP.
///   POST /api/patients/{patientId}/data-deletion-request            — Submit deletion request.
///   GET  /api/patients/{patientId}/data-deletion-request/{requestId} — Check deletion status.
///
/// Admin endpoints:
///   POST /api/admin/data-access-requests/{requestId}/process   — Trigger export generation.
///   GET  /api/admin/data-access-requests                        — List all requests.
///   POST /api/admin/data-deletion-requests/{requestId}/process  — Trigger deletion pipeline.
///   GET  /api/admin/data-deletion-requests/{requestId}/verify   — Verify deletion completion.
///   GET  /api/admin/data-deletion-requests                      — List deletion requests.
/// </summary>
[ApiVersion("1.0")]
[ApiController]
[Produces("application/json")]
public sealed class PatientRightsController : ControllerBase
{
    private readonly IPatientDataExportService         _exportService;
    private readonly IPatientDataDeletionService       _deletionService;
    private readonly DeletionVerificationService       _verificationService;
    private readonly ILogger<PatientRightsController>  _logger;

    public PatientRightsController(
        IPatientDataExportService        exportService,
        IPatientDataDeletionService      deletionService,
        DeletionVerificationService      verificationService,
        ILogger<PatientRightsController> logger)
    {
        _exportService       = exportService;
        _deletionService     = deletionService;
        _verificationService = verificationService;
        _logger              = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/patients/{patientId}/data-access-request  (AC-1)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Submits a HIPAA Right of Access data export request for the specified patient.
    /// The system will generate the export within 30 days (NFR-044).
    /// </summary>
    /// <param name="patientId">ID of the patient requesting access to their data.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="202">Request accepted with 30-day SLA deadline.</response>
    /// <response code="400">Invalid patient ID.</response>
    /// <response code="401">Unauthenticated.</response>
    /// <response code="403">Insufficient role.</response>
    [HttpPost("api/patients/{patientId:guid}/data-access-request")]
    [Authorize(Policy = RbacPolicies.AnyAuthenticated)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SubmitRequest(Guid patientId, CancellationToken ct)
    {
        var requestedBy = User.Identity?.Name ?? User.FindFirst("sub")?.Value ?? "unknown";
        var request     = await _exportService.SubmitRequestAsync(patientId, requestedBy, ct);

        var statusUrl = Url.Action(
            nameof(GetRequestStatus),
            new { patientId, requestId = request.Id });

        return Accepted(new
        {
            requestId = request.Id,
            deadline  = request.DeadlineUtc,
            statusUrl,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/patients/{patientId}/data-access-request/{requestId}
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the status and metadata of a data access request.
    /// </summary>
    /// <param name="patientId">ID of the patient.</param>
    /// <param name="requestId">ID of the data access request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Request details returned.</response>
    /// <response code="404">Request not found.</response>
    [HttpGet("api/patients/{patientId:guid}/data-access-request/{requestId:guid}")]
    [Authorize(Policy = RbacPolicies.AnyAuthenticated)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRequestStatus(
        Guid patientId, Guid requestId, CancellationToken ct)
    {
        var request = await _exportService.GetRequestByIdAsync(requestId, ct);

        if (request is null || request.PatientId != patientId)
            return NotFound(new ErrorResponse
            {
                StatusCode = StatusCodes.Status404NotFound,
                Message    = "Data access request not found.",
            });

        return Ok(request);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/patients/{patientId}/data-access-request/{requestId}/download
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Downloads the completed data export ZIP archive for the specified request.
    /// Returns 404 when the export is not yet complete or the file is missing.
    /// </summary>
    /// <param name="patientId">ID of the patient (ownership check).</param>
    /// <param name="requestId">ID of the data access request.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">ZIP file stream returned.</response>
    /// <response code="404">Export not ready or file missing.</response>
    [HttpGet("api/patients/{patientId:guid}/data-access-request/{requestId:guid}/download")]
    [Authorize(Policy = RbacPolicies.AnyAuthenticated)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadExport(
        Guid patientId, Guid requestId, CancellationToken ct)
    {
        try
        {
            var stream = await _exportService.DownloadExportAsync(requestId, patientId, ct);

            return File(
                stream,
                contentType: "application/zip",
                fileDownloadName: "patient_data_export.zip",
                enableRangeProcessing: false);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogInformation("DownloadExport: {Message}", ex.Message);
            return NotFound(new ErrorResponse
            {
                StatusCode = StatusCodes.Status404NotFound,
                Message    = "Export is not yet available.",
            });
        }
        catch (FileNotFoundException)
        {
            return NotFound(new ErrorResponse
            {
                StatusCode = StatusCodes.Status404NotFound,
                Message    = "Export file not found. Please contact support.",
            });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/admin/data-access-requests/{requestId}/process  (Admin)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Admin action to trigger export generation for a submitted data access request.
    /// </summary>
    /// <param name="requestId">ID of the data access request to process.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Request processed successfully.</response>
    /// <response code="404">Request not found.</response>
    [HttpPost("api/admin/data-access-requests/{requestId:guid}/process")]
    [Authorize(Policy = RbacPolicies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ProcessRequest(Guid requestId, CancellationToken ct)
    {
        var processedBy = User.Identity?.Name ?? User.FindFirst("sub")?.Value ?? "admin";

        try
        {
            var request = await _exportService.ProcessRequestAsync(requestId, processedBy, ct);
            return Ok(request);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new ErrorResponse
            {
                StatusCode = StatusCodes.Status404NotFound,
                Message    = ex.Message,
            });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/admin/data-access-requests  (Admin)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a paginated list of all data access requests.  Supports filtering by
    /// status and overdue flag (past 30-day deadline and not completed).
    /// </summary>
    /// <param name="status">Optional status filter: "Submitted", "Processing", "Completed", "Failed".</param>
    /// <param name="overdue">When true, returns only requests past their 30-day deadline that are not completed.</param>
    /// <param name="page">1-based page number (default: 1).</param>
    /// <param name="pageSize">Page size (default: 25, max: 100).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Paginated list of requests.</response>
    [HttpGet("api/admin/data-access-requests")]
    [Authorize(Policy = RbacPolicies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListRequests(
        [FromQuery] string? status,
        [FromQuery] bool overdue  = false,
        [FromQuery] int  page     = 1,
        [FromQuery] int  pageSize = 25,
        CancellationToken ct      = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page     = Math.Max(1, page);

        var all = (await _exportService.GetRequestsAsync(status, overdue, ct))
            .Where(r => r.RequestType == "DataAccess")
            .ToList();

        var paged = all
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new
        {
            totalCount = all.Count,
            page,
            pageSize,
            items = paged,
        });
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // HIPAA RIGHT TO DELETION  (US_094, NFR-045)
    // ═══════════════════════════════════════════════════════════════════════════

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/patients/{patientId}/data-deletion-request  (AC-3)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Submits a HIPAA Right to Deletion request for the patient.
    /// The request is created with a 30-day SLA deadline and "Submitted" status.
    /// An admin must call the process endpoint to execute the deletion pipeline.
    /// </summary>
    [HttpPost("api/patients/{patientId:guid}/data-deletion-request")]
    [Authorize(Policy = RbacPolicies.PatientOnly)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SubmitDeletionRequest(
        Guid patientId, CancellationToken ct)
    {
        var requestedBy = User.Identity?.Name ?? patientId.ToString();
        var request     = await _deletionService.SubmitDeletionRequestAsync(patientId, requestedBy, ct);

        return CreatedAtAction(
            nameof(GetDeletionRequest),
            new { patientId, requestId = request.Id },
            new
            {
                requestId   = request.Id,
                status      = request.Status,
                requestType = request.RequestType,
                deadlineUtc = request.DeadlineUtc,
            });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/patients/{patientId}/data-deletion-request/{requestId}
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the current status and SLA deadline for a data deletion request.
    /// </summary>
    [HttpGet("api/patients/{patientId:guid}/data-deletion-request/{requestId:guid}")]
    [Authorize(Policy = RbacPolicies.AnyAuthenticated)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDeletionRequest(
        Guid patientId, Guid requestId, CancellationToken ct)
    {
        var request = await _exportService.GetRequestByIdAsync(requestId, ct);
        if (request is null || request.PatientId != patientId || request.RequestType != "DataDeletion")
            return NotFound();

        return Ok(new
        {
            requestId      = request.Id,
            status         = request.Status,
            requestType    = request.RequestType,
            requestedAtUtc = request.RequestedAtUtc,
            deadlineUtc    = request.DeadlineUtc,
            completedAtUtc = request.CompletedAtUtc,
            failureReason  = request.FailureReason,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/admin/data-deletion-requests/{requestId}/process  (AC-3)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Executes the six-phase patient data deletion pipeline for the specified request.
    /// Returns a detailed <see cref="UPACIP.Service.PatientRights.Models.DeletionResult"/>.
    /// </summary>
    [HttpPost("api/admin/data-deletion-requests/{requestId:guid}/process")]
    [Authorize(Policy = RbacPolicies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ProcessDeletion(Guid requestId, CancellationToken ct)
    {
        try
        {
            var processedBy = User.Identity?.Name ?? "admin";
            var result      = await _deletionService.ProcessDeletionAsync(requestId, processedBy, ct);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/admin/data-deletion-requests/{requestId}/verify  (AC-4)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a post-deletion verification scan and returns whether the patient's data
    /// has been fully removed from the primary store, cache, and file system.
    /// </summary>
    [HttpGet("api/admin/data-deletion-requests/{requestId:guid}/verify")]
    [Authorize(Policy = RbacPolicies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VerifyDeletion(Guid requestId, CancellationToken ct)
    {
        var request = await _exportService.GetRequestByIdAsync(requestId, ct);
        if (request is null || request.RequestType != "DataDeletion")
            return NotFound();

        var result = await _verificationService.VerifyDeletionAsync(request.PatientId, ct);
        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/admin/data-deletion-requests  (admin list)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a paged list of all data deletion requests across all patients.
    /// </summary>
    [HttpGet("api/admin/data-deletion-requests")]
    [Authorize(Policy = RbacPolicies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListDeletionRequests(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page     = Math.Max(page, 1);

        var all = (await _exportService.GetRequestsAsync(null, false, ct))
            .Where(r => r.RequestType == "DataDeletion")
            .ToList();
        var total = all.Count;
        var paged = all.Skip((page - 1) * pageSize).Take(pageSize);

        return Ok(new
        {
            page,
            pageSize,
            total,
            items = paged,
        });
    }
}
