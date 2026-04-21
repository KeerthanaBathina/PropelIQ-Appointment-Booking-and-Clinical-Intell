using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UPACIP.Api.Authorization;
using UPACIP.Api.Middleware;
using UPACIP.Api.Models;
using UPACIP.DataAccess;
using UPACIP.Service.Appointments;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Bidirectional intake mode-switch endpoints (US_029, FR-028).
///
/// Endpoints:
///   POST /api/intake/mode/switch-manual      — AI → Manual: merge and return prefill payload (AC-1, AC-3, AC-4)
///   POST /api/intake/manual/switch-ai        — Manual → AI: merge and resume AI session (AC-2, AC-3, AC-4)
///   GET  /api/intake/mode/ai-availability    — AI availability probe (EC-2)
///
/// Route note:
///   <c>POST /api/intake/manual/switch-ai</c> is mounted under the /manual sub-path so the
///   frontend hook <c>useIntakeModeSwitch.switchToAI()</c> can call it with a single relative
///   path ("/api/intake/manual/switch-ai") matching the ManualIntakeController namespace.
///
/// Authorization: Patient role only (OWASP A01).
///   PatientId is always resolved server-side from the JWT — never from the request body.
///
/// Idempotency (EC-2):
///   Both switch operations are safe to replay. Repeated calls with the same patient ID
///   return the current merged state without creating duplicate rows.
///
/// AI unavailability (EC-2):
///   <c>POST /api/intake/manual/switch-ai</c> returns HTTP 503 when the AI service is not
///   reachable. The FE hook catches 503 and sets <c>aiAvailable = false</c> to disable
///   the button (UXR-605).
/// </summary>
[ApiController]
[Authorize(Policy = RbacPolicies.PatientOnly)]
[Produces("application/json")]
public sealed class IntakeModeSwitchController : ControllerBase
{
    private readonly IIntakeModeSwitchService            _switchService;
    private readonly ApplicationDbContext                _db;
    private readonly ILogger<IntakeModeSwitchController> _logger;

    public IntakeModeSwitchController(
        IIntakeModeSwitchService            switchService,
        ApplicationDbContext                db,
        ILogger<IntakeModeSwitchController> logger)
    {
        _switchService = switchService;
        _db            = db;
        _logger        = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/intake/mode/switch-manual — AI → Manual (AC-1, AC-3, AC-4)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Switches the patient from AI intake to the manual form.
    ///
    /// Merges all AI-collected field values into the manual draft snapshot and returns
    /// the combined field map plus prefill key metadata. Conflicts between AI values and
    /// previously edited manual values are returned in <c>Conflicts</c> (EC-1, AC-4).
    ///
    /// Idempotent: calling this when no active AI session exists returns an empty field map.
    /// </summary>
    /// <response code="200">Merged field payload ready to pre-populate the manual form.</response>
    /// <response code="404">Patient profile not found.</response>
    [HttpPost("api/intake/mode/switch-manual")]
    [ProducesResponseType(typeof(SwitchToManualModeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SwitchToManual(CancellationToken ct)
    {
        var patientId = await ResolvePatientIdAsync(ct);
        if (patientId is null)
            return NotFound(BuildError(404, "Patient profile not found."));

        var result = await _switchService.SwitchToManualAsync(patientId.Value, ct);

        _logger.LogInformation(
            "IntakeModeSwitch: AI→Manual; patientId={PatientId}; prefilledCount={Count}; conflicts={ConflictCount}.",
            patientId, result.PrefilledFields.Count, result.Conflicts.Count);

        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/intake/manual/switch-ai — Manual → AI (AC-2, AC-3, AC-4)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Switches the patient from the manual form to AI intake.
    ///
    /// Merges the supplied manual field values into the active AI session (or creates a new one),
    /// computes the next uncollected field, and returns the session ID to resume (AC-2).
    ///
    /// Conflicts between manual values and earlier AI values are returned in <c>Conflicts</c>
    /// for UI source attribution (EC-1, AC-4).
    /// </summary>
    /// <response code="200">Session resumed; returns sessionId and next field.</response>
    /// <response code="503">AI service is unavailable; switch-to-AI is rejected (EC-2).</response>
    /// <response code="404">Patient profile not found.</response>
    [HttpPost("api/intake/manual/switch-ai")]
    [ProducesResponseType(typeof(SwitchToAIResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SwitchToAI(
        [FromBody] SwitchToAIRequest request,
        CancellationToken ct)
    {
        var patientId = await ResolvePatientIdAsync(ct);
        if (patientId is null)
            return NotFound(BuildError(404, "Patient profile not found."));

        var result = await _switchService.SwitchToAIAsync(patientId.Value, request, ct);

        if (result is null)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                BuildError(503, "AI intake is temporarily unavailable. Please continue with the manual form."));
        }

        _logger.LogInformation(
            "IntakeModeSwitch: Manual→AI; patientId={PatientId}; sessionId={SessionId}; nextField={NextField}; conflicts={ConflictCount}.",
            patientId, result.SessionId, result.NextField ?? "none", result.Conflicts.Count);

        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/intake/mode/ai-availability — probe (EC-2)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a lightweight AI availability indicator so the UI can disable the
    /// switch-to-AI button before a patient triggers a failing transition (EC-2).
    ///
    /// The result is cached for 30 seconds in Redis to prevent gateway hammering.
    /// </summary>
    /// <response code="200">Availability response; check <c>Available</c> property.</response>
    [HttpGet("api/intake/mode/ai-availability")]
    [ProducesResponseType(typeof(AIAvailabilityResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAIAvailability(CancellationToken ct)
    {
        var result = await _switchService.CheckAIAvailabilityAsync(ct);
        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private string? GetUserEmail()
        => User.FindFirst(JwtRegisteredClaimNames.Email)?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;

    private async Task<Guid?> ResolvePatientIdAsync(CancellationToken ct)
    {
        var email = GetUserEmail();
        if (email is null) return null;

        return await _db.Patients
            .AsNoTracking()
            .Where(p => p.Email == email && p.DeletedAt == null)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);
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
