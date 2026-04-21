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
/// Manual intake form endpoints — draft load, autosave, and submission (US_028, FR-027, FR-029, FR-030, FR-031).
///
/// Endpoints:
///   GET  /api/intake/manual/draft    — load or restore draft with AI-prefilled metadata (EC-1, AC-2)
///   POST /api/intake/manual/draft    — autosave partial form state at 30-second cadence (UXR-004, EC-1)
///   POST /api/intake/manual/submit   — validate and finalize intake; triggers staff-review event (AC-3, AC-4)
///
/// Authorization: Patient role only.
///   PatientId is always resolved server-side from the JWT email claim
///   — never trusted from the URL or body (OWASP A01 — Broken Access Control).
///
/// Idempotency (EC-2):
///   POST /api/intake/manual/submit is safe to retry. When the patient's intake record
///   already has a CompletedAt timestamp the service returns the existing completion
///   data without performing any writes.
///
/// Validation (AC-3):
///   Field-level errors from IManualIntakeService.SubmitAsync are returned as HTTP 422
///   using the shared ErrorResponse.ValidationErrors dictionary so the UI can render
///   inline error messages from the same structure used by FluentValidation 400 responses.
/// </summary>
[ApiController]
[Route("api/intake/manual")]
[Authorize(Policy = RbacPolicies.PatientOnly)]
[Produces("application/json")]
public sealed class ManualIntakeController : ControllerBase
{
    private readonly IManualIntakeService            _manualIntakeService;
    private readonly ApplicationDbContext            _db;
    private readonly ILogger<ManualIntakeController> _logger;

    public ManualIntakeController(
        IManualIntakeService            manualIntakeService,
        ApplicationDbContext            db,
        ILogger<ManualIntakeController> logger)
    {
        _manualIntakeService = manualIntakeService;
        _db                  = db;
        _logger              = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/intake/manual/draft — load or restore draft (EC-1, AC-2)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads the patient's current manual intake draft.
    ///
    /// Returns the saved field values plus a list of field names that were pre-populated
    /// from a prior AI intake session so the UI can render prefill badges (AC-2).
    ///
    /// When no draft exists a 404 is returned — the UI begins with a blank form
    /// pre-seeded with the authenticated patient's profile data.
    /// </summary>
    /// <response code="200">Draft loaded. Fields include AI-prefilled metadata.</response>
    /// <response code="404">No in-progress draft found for this patient; start a fresh form.</response>
    [HttpGet("draft")]
    [ProducesResponseType(typeof(ManualIntakeDraftResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDraft(CancellationToken ct)
    {
        var patientId = await ResolvePatientIdAsync(ct);
        if (patientId is null)
            return NotFound(BuildError(404, "Patient profile not found."));

        var draft = await _manualIntakeService.LoadDraftAsync(patientId.Value, ct);
        if (draft is null)
            return NotFound(BuildError(404, "No in-progress manual intake draft found."));

        return Ok(draft);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/intake/manual/draft — autosave (UXR-004, EC-1)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Persists a partial autosave update into the patient's manual intake draft.
    ///
    /// Called by the UI every 30 seconds (UXR-004). Null fields in the request body are
    /// silently ignored — only supplied values are merged into the snapshot.
    ///
    /// Creates the draft record on the first call; updates it on subsequent calls
    /// without producing duplicate rows (EC-1 idempotent upsert).
    /// </summary>
    /// <response code="200">Draft saved; returns the UTC timestamp for the UI autosave label.</response>
    [HttpPost("draft")]
    [ProducesResponseType(typeof(SaveManualIntakeDraftResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveDraft(
        [FromBody] SaveManualIntakeDraftRequest request,
        CancellationToken ct)
    {
        var patientId = await ResolvePatientIdAsync(ct);
        if (patientId is null)
            return NotFound(BuildError(404, "Patient profile not found."));

        var result = await _manualIntakeService.SaveDraftAsync(patientId.Value, request, ct);

        _logger.LogDebug(
            "ManualIntake: autosave for patient {PatientId}; savedAt={SavedAt}.",
            patientId, result.LastSavedAt);

        return Ok(result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/intake/manual/submit — finalize intake (AC-3, AC-4, EC-2)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates all mandatory fields and, if valid, marks the intake as completed.
    ///
    /// On validation failure returns HTTP 422 with a <c>ValidationErrors</c> dictionary
    /// keyed by camelCase field name — identical shape to FluentValidation 400 responses
    /// so the UI renders them inline (AC-3).
    ///
    /// Retry-safe: if the intake was already completed by a prior call the service returns
    /// the existing completion record without any additional writes (EC-2).
    ///
    /// A structured log event is emitted after completion for staff-review workflows (AC-4).
    /// </summary>
    /// <response code="200">Intake finalized. Returns intakeDataId and completedAt timestamp.</response>
    /// <response code="422">One or more mandatory fields failed validation. See ValidationErrors.</response>
    [HttpPost("submit")]
    [ProducesResponseType(typeof(SubmitManualIntakeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Submit(
        [FromBody] SubmitManualIntakeRequest request,
        CancellationToken ct)
    {
        var patientId = await ResolvePatientIdAsync(ct);
        if (patientId is null)
            return NotFound(BuildError(404, "Patient profile not found."));

        var (response, errors) = await _manualIntakeService.SubmitAsync(patientId.Value, request, ct);

        if (errors is { Count: > 0 })
        {
            var validationDict = errors
                .GroupBy(e => e.Field)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.Message).ToArray());

            return UnprocessableEntity(new ErrorResponse
            {
                StatusCode       = 422,
                Message          = "One or more required fields are missing or invalid.",
                CorrelationId    = HttpContext.Items[CorrelationIdMiddleware.ItemsKey]?.ToString()
                                   ?? Guid.NewGuid().ToString(),
                Timestamp        = DateTimeOffset.UtcNow,
                ValidationErrors = validationDict,
            });
        }

        _logger.LogInformation(
            "ManualIntake: intake completed; patientId={PatientId}, intakeDataId={IntakeDataId}.",
            patientId, response!.IntakeDataId);

        return Ok(response);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/intake/insurance/precheck — real-time debounced pre-check (US_031 AC-2, FR-033)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a soft insurance pre-check on the supplied provider and policy number against
    /// a known-valid set of dummy records and returns an inline status for the patient UI.
    ///
    /// Called on 800ms debounce from the manual intake insurance fields.
    /// Does not modify any database records; the result is advisory only.
    ///
    /// Statuses:
    ///   "valid"        — provider and policy matched known-valid prefixes (green badge).
    ///   "needs-review" — partial or unrecognized data; flagged for staff review.
    ///   "skipped"      — both fields empty; insurance section not applicable.
    /// </summary>
    /// <response code="200">Pre-check result returned.</response>
    [HttpPost("/api/intake/insurance/precheck")]
    [ProducesResponseType(typeof(InsurancePrecheckResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RunInsurancePrecheck(
        [FromBody] InsurancePrecheckRequest request,
        CancellationToken ct)
    {
        var patientId = await ResolvePatientIdAsync(ct);
        if (patientId is null)
            return NotFound(BuildError(404, "Patient profile not found."));

        var result = await _manualIntakeService.RunInsurancePrecheckAsync(
            patientId.Value,
            request.InsuranceProvider,
            request.PolicyNumber,
            ct);

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
