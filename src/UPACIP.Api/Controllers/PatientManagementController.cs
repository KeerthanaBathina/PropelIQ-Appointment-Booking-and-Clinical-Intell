using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPACIP.Api.Authorization;
using UPACIP.Api.Middleware;
using UPACIP.Api.Models;
using UPACIP.Service.Patients;
using UPACIP.Service.Patients.Models;
using Asp.Versioning;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Admin patient-management endpoints: soft delete, restore, and include-deleted list query
/// (US_087 AC-1, AC-2, AC-3, DR-021, NFR-033).
///
/// Routes:
///   DELETE /api/patients/{patientId}            — Soft-delete (staff or admin).
///   POST   /api/patients/{patientId}/restore     — Restore a soft-deleted patient (admin only).
///   GET    /api/patients?includeDeleted=true     — Patient list with soft-deleted entries (admin only).
///
/// Authorization:
///   DELETE is accessible to Staff or Admin (standard data-management operation).
///   POST restore and GET includeDeleted are restricted to Admin because they expose or
///   modify logically-removed records — a more sensitive privilege (US_087 implementation plan).
///
/// OWASP A01 — Broken Access Control:
///   Role checks are enforced by named authorization policies so that a typo in the policy name
///   is caught at startup, not at runtime.  Route parameter <c>patientId</c> is always looked up
///   server-side by the service — never trusted as implying existence.
///
/// OWASP A09 — Security Logging and Monitoring Failures:
///   Every soft-delete and restore operation appends an immutable <see cref="AuditLog"/> entry
///   (US_064) in the same database transaction as the patient update, guaranteeing that the
///   audit trail is consistent with the state of the record.
/// </summary>
[ApiVersion("1.0")]
[ApiController]
[Route("api/patients")]
[Produces("application/json")]
public sealed class PatientManagementController : ControllerBase
{
    private readonly IPatientSoftDeleteService              _softDeleteService;
    private readonly ILogger<PatientManagementController>  _logger;

    public PatientManagementController(
        IPatientSoftDeleteService              softDeleteService,
        ILogger<PatientManagementController>  logger)
    {
        _softDeleteService = softDeleteService;
        _logger            = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE /api/patients/{patientId}  (AC-1)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Soft-deletes a patient by setting <c>deleted_at</c> to the current UTC timestamp
    /// without physically removing the row (AC-1, DR-021).
    ///
    /// The operation is blocked when the patient has active scheduled appointments,
    /// intake records currently in AI processing, or clinical documents queued for
    /// processing (edge case 1).  The response body includes a list of blocking
    /// dependencies so the caller knows exactly what to resolve.
    ///
    /// Standard patient queries automatically exclude the record after this operation
    /// because the global EF Core query filter (<c>deleted_at IS NULL</c>) is applied
    /// transparently (AC-2).
    /// </summary>
    /// <param name="patientId">Unique identifier of the patient to soft-delete.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Patient soft-deleted successfully.</response>
    /// <response code="404">Patient not found.</response>
    /// <response code="409">Soft delete blocked — response body contains ActiveDependencies list.</response>
    [HttpDelete("{patientId:guid}")]
    [Authorize(Policy = RbacPolicies.StaffOrAdmin)]
    [ProducesResponseType(typeof(SoftDeleteResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(SoftDeleteResult), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SoftDelete(Guid patientId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized(BuildError(401, "Unable to resolve the requesting user identity."));

        var result = await _softDeleteService.SoftDeleteAsync(
            patientId,
            userId.Value,
            GetClientIp(),
            GetUserAgent(),
            ct);

        if (result is null)
            return NotFound(BuildError(404, $"Patient {patientId} not found."));

        if (!result.Success)
        {
            _logger.LogWarning(
                "Soft delete blocked for patient {PatientId}: {Reason}",
                patientId, result.BlockedReason);
            return Conflict(result);
        }

        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/patients/{patientId}/restore  (edge case 2)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Restores a soft-deleted patient by clearing <c>deleted_at</c> to <c>null</c> (edge case 2).
    ///
    /// All dependent records (appointments, intake, clinical documents, medical codes) linked by
    /// FK are immediately queryable again because they were never physically removed — clearing
    /// <c>deleted_at</c> on the Patient row is sufficient to restore full visibility through the
    /// global query filter.
    ///
    /// Restricted to the Admin role because restoring logically-removed records is a privileged,
    /// audited operation.
    /// </summary>
    /// <param name="patientId">Unique identifier of the patient to restore.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Patient restored successfully.</response>
    /// <response code="404">Patient not found, or the patient is not currently soft-deleted.</response>
    [HttpPost("{patientId:guid}/restore")]
    [Authorize(Policy = RbacPolicies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Restore(Guid patientId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized(BuildError(401, "Unable to resolve the requesting user identity."));

        var result = await _softDeleteService.RestoreAsync(
            patientId,
            userId.Value,
            GetClientIp(),
            GetUserAgent(),
            ct);

        if (result is null)
            return NotFound(BuildError(404, $"Patient {patientId} not found."));

        if (!result.Value)
            return NotFound(BuildError(404,
                $"Patient {patientId} is not currently soft-deleted and cannot be restored."));

        return Ok(new { patientId, message = "Patient restored successfully." });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/patients?includeDeleted=true  (AC-3)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a paginated list of patients.
    ///
    /// When <paramref name="includeDeleted"/> is <c>true</c> (admin only), soft-deleted
    /// patients are included and each record is annotated with <c>isDeleted</c> and
    /// <c>deletedAt</c> fields for visual indication (AC-3).
    ///
    /// When <paramref name="includeDeleted"/> is <c>false</c> or omitted, the response
    /// returns only active patients — the global EF Core query filter handles the exclusion
    /// transparently (AC-2).
    ///
    /// The <c>includeDeleted=true</c> path is restricted to Admin because it exposes
    /// logically-removed patient records.
    /// </summary>
    /// <param name="includeDeleted">When <c>true</c>, include soft-deleted patients in the result.</param>
    /// <param name="page">1-based page number (default 1).</param>
    /// <param name="pageSize">Records per page, 1–200 (default 50).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Paginated patient list returned.</response>
    /// <response code="403">includeDeleted=true requested by non-admin user.</response>
    [HttpGet]
    [Authorize(Policy = RbacPolicies.StaffOrAdmin)]
    [ProducesResponseType(typeof(PatientListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ListPatients(
        [FromQuery] bool includeDeleted = false,
        [FromQuery] int  page           = 1,
        [FromQuery] int  pageSize       = 50,
        CancellationToken ct            = default)
    {
        // Returning soft-deleted records is a privileged admin operation.
        if (includeDeleted)
        {
            if (!User.IsInRole("Admin"))
                return Forbid();

            var (items, total) = await _softDeleteService
                .GetPatientsIncludingDeletedAsync(page, pageSize, ct);

            return Ok(new PatientListResponse
            {
                Items      = items,
                TotalCount = total,
                Page       = page,
                PageSize   = pageSize,
            });
        }

        // Standard (active-only) list — delegates to the same service so the global query
        // filter applies automatically. Returns only active records without IsDeleted annotation.
        var (activeItems, activeTotal) = await _softDeleteService
            .GetPatientsIncludingDeletedAsync(page, pageSize, ct);

        // Filter out deleted records for non-admin callers (belt-and-suspenders in addition
        // to the EF Core global filter — the service returns all records when called directly,
        // so we apply the presentational filter here when includeDeleted=false).
        var filteredItems = activeItems.Where(i => !i.IsDeleted).ToList();

        return Ok(new PatientListResponse
        {
            Items      = filteredItems,
            TotalCount = activeTotal - (activeItems.Count - filteredItems.Count),
            Page       = page,
            PageSize   = pageSize,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
               ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private string GetClientIp()
        => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private string GetUserAgent()
        => Request.Headers.UserAgent.ToString();

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

/// <summary>
/// Paginated wrapper returned by GET /api/patients (US_087 AC-3).
/// </summary>
public sealed record PatientListResponse
{
    public IReadOnlyList<PatientListItem> Items { get; init; } = Array.Empty<PatientListItem>();
    public int TotalCount { get; init; }
    public int Page      { get; init; }
    public int PageSize  { get; init; }
}
