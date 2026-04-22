using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using UPACIP.Api.Authorization;
using UPACIP.Api.Middleware;
using UPACIP.Api.Models;
using UPACIP.Service.AI;
using UPACIP.Service.Consolidation;

namespace UPACIP.Api.Controllers;

/// <summary>
/// REST endpoints for the manual fallback workflow and AI availability monitoring
/// (US_046, AC-1, AC-3, AC-4, FR-093, NFR-030, NFR-034).
///
/// Routes:
///   GET  /api/patients/{patientId}/profile/low-confidence   — Low-confidence items for manual review (AC-1).
///   POST /api/patients/{patientId}/profile/manual-verify    — Save manually verified/corrected entries (AC-3).
///   GET  /api/health/ai                                     — AI gateway availability status (AC-4, NFR-030).
///
/// Authorization:
///   <c>GET/POST /api/patients/…</c> — StaffOrAdmin role (RBAC FR-002).
///   <c>GET /api/health/ai</c>       — AllowAnonymous (operational status readable without auth).
///
/// OWASP A01 — Broken Access Control:
///   PatientId in the route is always joined to the DB query — ownership is validated server-side.
///   StaffUserId for attribution is extracted from the JWT, never from the request body.
///
/// OWASP A03 — Injection:
///   All free-text inputs (ResolutionNotes, CorrectedValue) pass through FluentValidation before
///   the controller method executes. Parameterised EF Core queries prevent SQL injection.
/// </summary>
[ApiController]
[Produces("application/json")]
public sealed class ManualFallbackController : ControllerBase
{
    private readonly IConsolidationConfidenceService _confidenceService;
    private readonly IAiHealthCheckService           _aiHealthService;
    private readonly ILogger<ManualFallbackController> _logger;

    public ManualFallbackController(
        IConsolidationConfidenceService  confidenceService,
        IAiHealthCheckService            aiHealthService,
        ILogger<ManualFallbackController> logger)
    {
        _confidenceService = confidenceService;
        _aiHealthService   = aiHealthService;
        _logger            = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/patients/{patientId}/profile/low-confidence
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all non-archived, unverified extracted-data entries for a patient whose
    /// AI confidence score is below the 80% threshold (AC-1).
    /// </summary>
    /// <param name="patientId">Target patient primary key.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Low-confidence items list with total count.</returns>
    [HttpGet("api/patients/{patientId:guid}/profile/low-confidence")]
    [Authorize(Policy = RbacPolicies.StaffOrAdmin)]
    [ProducesResponseType(typeof(LowConfidenceResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetLowConfidenceItemsAsync(Guid patientId, CancellationToken ct)
    {
        _logger.LogInformation(
            "ManualFallbackController: GET low-confidence items. PatientId={PatientId}",
            patientId);

        var result = await _confidenceService.GetLowConfidenceItemsAsync(patientId, ct);
        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/patients/{patientId}/profile/manual-verify
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Saves a batch of manually verified or corrected extracted-data entries (AC-3, FR-093).
    ///
    /// Each confirmed entry is updated with <c>VerificationStatus = ManualVerified</c>,
    /// staff attribution, and an immutable audit log entry. All updates execute in a single
    /// database transaction. Idempotency is enforced via the optional <c>Idempotency-Key</c> header.
    /// </summary>
    /// <param name="patientId">Target patient primary key.</param>
    /// <param name="request">Verification payload validated by <c>ManualVerifyRequestDtoValidator</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>204 No Content on success; 200 when idempotency key was already processed.</returns>
    [HttpPost("api/patients/{patientId:guid}/profile/manual-verify")]
    [Authorize(Policy = RbacPolicies.StaffOrAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ManualVerifyAsync(
        Guid                   patientId,
        [FromBody] ManualVerifyRequestDto request,
        CancellationToken      ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            _logger.LogWarning("ManualFallbackController: JWT did not contain a valid user ID.");
            return Unauthorized(BuildError(StatusCodes.Status401Unauthorized, "User identity could not be resolved from token."));
        }

        // Read idempotency key from header if supplied (NFR-034).
        var idempotencyKey = Request.Headers.TryGetValue("Idempotency-Key", out var hv)
            ? hv.FirstOrDefault()
            : null;

        _logger.LogInformation(
            "ManualFallbackController: POST manual-verify. PatientId={PatientId}, EntryCount={Count}",
            patientId, request.Entries.Count);

        var applied = await _confidenceService.ManualVerifyEntriesAsync(
            patientId, request, userId.Value, idempotencyKey, ct);

        // false means idempotency key was already processed — return 200 instead of 204
        return applied ? NoContent() : Ok();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/health/ai
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the current AI gateway availability status cached in Redis (AC-4, NFR-030).
    ///
    /// Accessed by the frontend to decide whether to display the "AI unavailable — switch to manual"
    /// banner. AllowAnonymous so the status is readable even during auth disruptions.
    /// </summary>
    [HttpGet("api/health/ai")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AiHealthStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAiHealthAsync(CancellationToken ct)
    {
        var status = await _aiHealthService.GetHealthStatusAsync(ct);
        return Ok(status);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private ErrorResponse BuildError(int status, string message)
    {
        var correlationId = HttpContext.Items[CorrelationIdMiddleware.ItemsKey]?.ToString()
                         ?? Guid.NewGuid().ToString();
        return new ErrorResponse
        {
            StatusCode    = status,
            Message       = message,
            CorrelationId = correlationId,
            Timestamp     = DateTimeOffset.UtcNow,
        };
    }
}
