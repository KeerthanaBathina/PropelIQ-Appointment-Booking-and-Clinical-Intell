using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UPACIP.Api.Authorization;
using UPACIP.Api.Middleware;
using UPACIP.Api.Models;
using UPACIP.DataAccess;
using UPACIP.Service.Appointments; // includes AI intake DTOs (AIIntakeDtos.cs)

namespace UPACIP.Api.Controllers;

/// <summary>
/// AI Conversational Intake session and message endpoints (US_027, FR-026, FR-029, AC-1–AC-5).
///
/// Endpoints:
///   POST   /api/intake/sessions                              — start or resume session (AC-1, EC-2)
///   POST   /api/intake/sessions/{sessionId}/messages        — send patient message (AC-2)
///   GET    /api/intake/sessions/{sessionId}/summary         — get review summary (AC-4)
///   POST   /api/intake/sessions/{sessionId}/complete        — finalize and persist intake (AC-4)
///   POST   /api/intake/sessions/{sessionId}/switch-manual   — transfer to manual form (FL-004)
///   POST   /api/intake/sessions/{sessionId}/autosave        — 30-second boundary heartbeat (US_030, FR-035)
///
/// Authorization: Patient role only (FR-026).
///   PatientId is always resolved server-side from the JWT email claim
///   — never trusted from the URL or body (OWASP A01 — Broken Access Control).
///
/// Payload bounds (AIR-S06):
///   Message content is capped at 1 000 chars by <see cref="AIIntakeMessageRequest"/> validation.
///   All session IDs are validated as GUIDs before use.
///
/// Logging:
///   Structured log events include sessionId and correlationId.
///   Patient name, DOB, and other PII are never written to logs (AIR-S01).
///
/// Performance (AC-2):
///   Target: AI exchange round-trip ≤ 1 second P50. The AI layer has its own timeout
///   (10 s by default in AiGatewaySettings) which enforces an upper-bound.
/// </summary>
[ApiController]
[Route("api/intake/sessions")]
[Authorize(Policy = RbacPolicies.PatientOnly)]
[Produces("application/json")]
public sealed class AIIntakeController : ControllerBase
{
    private readonly IAIIntakeSessionService           _sessionService;
    private readonly IIntakeAutosaveService            _autosaveService;
    private readonly ApplicationDbContext              _db;
    private readonly ILogger<AIIntakeController>       _logger;

    public AIIntakeController(
        IAIIntakeSessionService     sessionService,
        IIntakeAutosaveService      autosaveService,
        ApplicationDbContext        db,
        ILogger<AIIntakeController> logger)
    {
        _sessionService  = sessionService;
        _autosaveService = autosaveService;
        _db              = db;
        _logger          = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/intake/sessions — start or resume (AC-1, EC-2)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Starts a new AI intake session or resumes the most recent active session
    /// for the authenticated patient (AC-1, EC-2).
    ///
    /// The greeting message explains the intake process. For resumed sessions,
    /// prior conversation history and progress state are restored.
    /// </summary>
    /// <response code="200">Session started or resumed successfully.</response>
    /// <response code="404">Patient profile not found for the authenticated user.</response>
    [HttpPost]
    [ProducesResponseType(typeof(StartAIIntakeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> StartOrResume(CancellationToken ct)
    {
        var email = GetUserEmail();
        if (email is null)
            return Unauthorized(BuildError(401, "JWT email claim missing."));

        var patient = await _db.Patients
            .AsNoTracking()
            .Where(p => p.Email == email && p.DeletedAt == null)
            .Select(p => new { p.Id })
            .FirstOrDefaultAsync(ct);

        if (patient is null)
        {
            _logger.LogWarning("AIIntake: no patient record for email {Email}.", email);
            return NotFound(BuildError(404, "Patient profile not found."));
        }

        var response = await _sessionService.StartOrResumeSessionAsync(patient.Id, email, ct);

        _logger.LogInformation(
            "AIIntake: session {SessionId} {Action} for patient {PatientId}.",
            response.SessionId, response.IsResumed ? "resumed" : "started", patient.Id);

        return Ok(response);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/intake/sessions/{sessionId}/messages — message exchange (AC-2)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Processes a patient message for the specified session.
    ///
    /// Invokes the AI orchestration layer, persists updated state, and returns
    /// the next AI prompt with progress counters (AC-2, AC-5).
    /// </summary>
    /// <param name="sessionId">Active session identifier.</param>
    /// <param name="request">Patient message (max 1 000 chars).</param>
    /// <response code="200">Exchange processed. Check <c>ShouldSwitchToManual</c> for fallback flag.</response>
    /// <response code="400">Empty or over-length message content.</response>
    /// <response code="404">Session not found or not owned by the caller.</response>
    [HttpPost("{sessionId:guid}/messages")]
    [ProducesResponseType(typeof(AIIntakeMessageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendMessage(
        Guid sessionId,
        [FromBody] AIIntakeMessageRequest request,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(BuildValidationError());

        var patientId = await ResolvePatientIdAsync(ct);
        if (patientId is null)
            return NotFound(BuildError(404, "Patient profile not found."));

        if (string.IsNullOrWhiteSpace(request.Content))
            return BadRequest(BuildError(400, "Message content must not be empty."));

        var result = await _sessionService.SendMessageAsync(sessionId, patientId.Value, request.Content, ct);
        if (result is null)
            return NotFound(BuildError(404, "Session not found or does not belong to this patient."));

        _logger.LogDebug(
            "AIIntake: message exchange; sessionId={SessionId}, field={FieldKey}, complete={IsComplete}, provider={Provider}.",
            sessionId, result.FieldKey, result.ExtractedValue is not null, result.Provider);

        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/intake/sessions/{sessionId}/summary — review summary (AC-4)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the AI-generated summary of all collected fields for patient review before
    /// final submission (AC-4).
    /// </summary>
    /// <param name="sessionId">Active session identifier.</param>
    /// <response code="200">Summary generated successfully.</response>
    /// <response code="404">Session not found or mandatory fields incomplete.</response>
    [HttpGet("{sessionId:guid}/summary")]
    [ProducesResponseType(typeof(AIIntakeSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSummary(Guid sessionId, CancellationToken ct)
    {
        var patientId = await ResolvePatientIdAsync(ct);
        if (patientId is null)
            return NotFound(BuildError(404, "Patient profile not found."));

        var result = await _sessionService.GetSummaryAsync(sessionId, patientId.Value, ct);
        if (result is null)
            return NotFound(BuildError(404, "Session not found or mandatory fields not yet complete."));

        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/intake/sessions/{sessionId}/complete — finalize intake (AC-4)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Finalizes the intake session and persists all collected fields to the
    /// <c>IntakeData</c> record in the database (AC-4, FR-029).
    /// </summary>
    /// <param name="sessionId">Active session identifier.</param>
    /// <response code="200">Intake finalized and persisted.</response>
    /// <response code="404">Session not found, not owned, or mandatory fields incomplete.</response>
    [HttpPost("{sessionId:guid}/complete")]
    [ProducesResponseType(typeof(CompleteIntakeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Complete(Guid sessionId, CancellationToken ct)
    {
        var patientId = await ResolvePatientIdAsync(ct);
        if (patientId is null)
            return NotFound(BuildError(404, "Patient profile not found."));

        var result = await _sessionService.CompleteSessionAsync(sessionId, patientId.Value, ct);
        if (result is null)
            return NotFound(BuildError(404,
                "Session not found, not owned by this patient, or mandatory fields are incomplete."));

        _logger.LogInformation(
            "AIIntake: session {SessionId} completed; intakeDataId={IntakeDataId}.",
            sessionId, result.IntakeDataId);

        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/intake/sessions/{sessionId}/autosave — 30-s boundary heartbeat (US_030)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Persists a 30-second boundary autosave heartbeat for an active AI intake session
    /// (US_030, FR-035, AC-1, AC-3).
    ///
    /// The AI session state is already kept current in Redis + DB by the message exchange.
    /// This endpoint confirms persistence, touches <c>LastAutoSavedAt</c>, and returns the
    /// server-confirmed timestamp so the client progress-header can show a fresh "Auto-saved" time.
    ///
    /// <b>Idempotency (EC-1)</b>: When <c>ClientSavedAt</c> is provided and the server record
    /// is already at or after that instant, the write is skipped and the existing timestamp is
    /// returned with <c>WasIdempotentReplay = true</c>.
    ///
    /// <b>Security</b>: sessionId is cross-checked against the JWT-resolved patientId (OWASP A01).
    /// </summary>
    /// <param name="sessionId">Active AI intake session GUID.</param>
    /// <param name="request">Boundary snapshot metadata from the client.</param>
    /// <response code="200">Autosave confirmed; <c>lastSavedAt</c> timestamp returned.</response>
    /// <response code="404">Session not found or does not belong to the authenticated patient.</response>
    [HttpPost("{sessionId:guid}/autosave")]
    [ProducesResponseType(typeof(AutosaveDraftResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AutosaveSession(
        Guid sessionId,
        [FromBody] AutosaveDraftRequest request,
        CancellationToken ct)
    {
        var patientId = await ResolvePatientIdAsync(ct);
        if (patientId is null)
            return NotFound(BuildError(404, "Patient profile not found."));

        DateTimeOffset? clientSavedAt = request.ClientSavedAt is not null
            && DateTimeOffset.TryParse(request.ClientSavedAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed)
                ? parsed
                : null;

        var result = await _autosaveService.SaveAISessionSnapshotAsync(
            sessionId,
            patientId.Value,
            request.CollectedCount,
            clientSavedAt,
            ct);

        if (result is null)
            return NotFound(BuildError(404, "Session not found or does not belong to this patient."));

        _logger.LogDebug(
            "AIIntake: autosave heartbeat; sessionId={SessionId}, wasReplay={Replay}, lastSavedAt={LastSavedAt}.",
            sessionId, result.WasIdempotentReplay, result.LastSavedAt);

        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/intake/sessions/{sessionId}/switch-manual — handoff (FL-004)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Switches the session to manual intake mode.
    /// Returns pre-filled field values so the manual form can be initialised
    /// without losing collected data (FL-004, US_028).
    /// </summary>
    /// <param name="sessionId">Active session identifier.</param>
    /// <response code="200">Session transferred; pre-filled fields returned.</response>
    /// <response code="404">Session not found or not owned by this patient.</response>
    [HttpPost("{sessionId:guid}/switch-manual")]
    [ProducesResponseType(typeof(SwitchToManualResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SwitchToManual(Guid sessionId, CancellationToken ct)
    {
        var patientId = await ResolvePatientIdAsync(ct);
        if (patientId is null)
            return NotFound(BuildError(404, "Patient profile not found."));

        var result = await _sessionService.SwitchToManualAsync(sessionId, patientId.Value, ct);
        if (result is null)
            return NotFound(BuildError(404, "Session not found or does not belong to this patient."));

        _logger.LogInformation(
            "AIIntake: session {SessionId} switched to manual form. fieldsCount={Count}.",
            sessionId, result.PrefilledFields.Count);

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

    private ErrorResponse BuildValidationError()
    {
        var errors = ModelState
            .Where(e => e.Value?.Errors.Count > 0)
            .ToDictionary(
                e => e.Key,
                e => e.Value!.Errors.Select(err => err.ErrorMessage).ToArray());

        return new ErrorResponse
        {
            StatusCode       = 400,
            Message          = "Validation failed.",
            CorrelationId    = HttpContext.Items[CorrelationIdMiddleware.ItemsKey]?.ToString() ?? Guid.NewGuid().ToString(),
            Timestamp        = DateTimeOffset.UtcNow,
            ValidationErrors = errors,
        };
    }
}
