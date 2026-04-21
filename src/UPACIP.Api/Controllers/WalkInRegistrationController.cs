using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UPACIP.Api.Authorization;
using UPACIP.Api.Models;
using UPACIP.Service.Appointments;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Staff-only endpoints for walk-in patient registration (US_022 FR-021, FR-022).
///
/// Endpoints:
///   GET    /api/staff/walkin/patients  — search existing patients by name, DOB, or phone (AC-2).
///   GET    /api/staff/walkin/slots     — return same-day available slots (AC-4).
///   POST   /api/staff/walkin           — create walk-in appointment + queue entry (AC-3, EC-2).
///
/// Authorization: Staff or Admin role only (EC-1).
///   Patient-role callers are rejected at the policy layer and logged as <c>WalkInUnauthorized</c>.
///   The exact staff-only message "Walk-in bookings are available through staff only." is returned
///   in the 403 response so the frontend can surface it per EC-1.
///
/// Correlation IDs (NFR-012, NFR-035):
///   All log events include the CorrelationId injected by <c>CorrelationIdMiddleware</c>.
/// </summary>
[ApiController]
[Route("api/staff/walkin")]
[Authorize(Policy = RbacPolicies.StaffOrAdmin)]
[Produces("application/json")]
public sealed class WalkInRegistrationController : ControllerBase
{
    private readonly IWalkInRegistrationService             _walkInService;
    private readonly ILogger<WalkInRegistrationController>  _logger;

    public WalkInRegistrationController(
        IWalkInRegistrationService              walkInService,
        ILogger<WalkInRegistrationController>   logger)
    {
        _walkInService = walkInService;
        _logger        = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/staff/walkin/patients — patient search (AC-2)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Searches existing patient records by name, DOB, or phone and returns up to 20
    /// lightweight summaries for staff selection.
    ///
    /// Query parameters:
    ///   q     — search term (min 2 characters, max 100).
    ///   field — name | dob | phone (default: name).
    ///
    /// Returns 200 OK with the matching patient list (empty array when no matches).
    /// Returns 400 Bad Request when the term is shorter than 2 characters.
    /// </summary>
    [HttpGet("patients")]
    [ProducesResponseType(typeof(IReadOnlyList<WalkInPatientSearchResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SearchPatients(
        [FromQuery] string q,
        [FromQuery] string field = "name",
        CancellationToken  cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 2)
        {
            return BadRequest(BuildError(
                StatusCodes.Status400BadRequest,
                "Search term must be at least 2 characters."));
        }

        var request = new WalkInPatientSearchRequest { Term = q.Trim(), Field = field };
        var results = await _walkInService.SearchPatientsAsync(request, cancellationToken);

        _logger.LogInformation(
            "WalkInController.SearchPatients: staff search field={Field}, returned {Count} result(s).",
            field, results.Count);

        return Ok(results);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/staff/walkin/slots — same-day slot availability (AC-4)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns same-day available slots for walk-in booking.
    /// When no same-day slots exist the response includes a <c>nextAvailableDate</c>
    /// so the frontend can display the "No same-day slots available" message (AC-4).
    /// </summary>
    [HttpGet("slots")]
    [ProducesResponseType(typeof(SameDaySlotsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSameDaySlots(CancellationToken cancellationToken)
    {
        var response = await _walkInService.GetSameDaySlotsAsync(cancellationToken);

        _logger.LogInformation(
            "WalkInController.GetSameDaySlots: {SlotCount} slot(s) available, NextAvailableDate={NextDate}.",
            response.Slots.Count, response.NextAvailableDate ?? "none");

        return Ok(response);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/staff/walkin — create walk-in appointment + queue entry (AC-3)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a walk-in appointment with <c>IsWalkIn = true</c> and automatically adds
    /// the patient to the arrival queue at Normal or Urgent priority.
    ///
    /// Either <c>patientId</c> (existing patient) or <c>newPatient</c> (inline creation)
    /// must be provided — not both.
    ///
    /// Status codes:
    ///   201 Created       — appointment and queue entry created (AC-3).
    ///   400 Bad Request   — validation failure or both/neither patient selectors supplied.
    ///   403 Forbidden     — patient-role caller (EC-1) — exact staff-only message returned.
    ///   409 Conflict      — selected slot is no longer available.
    ///   422 Unprocessable — urgent walk-in with no same-day capacity (EC-2); escalation required.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(WalkInBookingResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> BookWalkIn(
        [FromBody] WalkInBookingRequest request,
        CancellationToken               cancellationToken)
    {
        // ── Input validation: exactly one patient selector must be provided ──
        var hasExisting = request.PatientId.HasValue;
        var hasNew      = request.NewPatient is not null;

        if (!hasExisting && !hasNew)
        {
            return BadRequest(BuildError(
                StatusCodes.Status400BadRequest,
                "Either 'patientId' or 'newPatient' must be provided."));
        }

        if (hasExisting && hasNew)
        {
            return BadRequest(BuildError(
                StatusCodes.Status400BadRequest,
                "Provide either 'patientId' or 'newPatient', not both."));
        }

        var (outcome, response) = await _walkInService.BookWalkInAsync(request, cancellationToken);

        return outcome switch
        {
            WalkInBookingOutcome.Success =>
                Created($"/api/staff/walkin/{response!.AppointmentId}", response),

            WalkInBookingOutcome.SlotUnavailable =>
                Conflict(BuildError(
                    StatusCodes.Status409Conflict,
                    "The selected slot is no longer available. Please refresh same-day slots and try again.")),

            WalkInBookingOutcome.UrgentEscalation =>
                UnprocessableEntity(BuildError(
                    StatusCodes.Status422UnprocessableEntity,
                    "No same-day slots are available for this urgent walk-in. " +
                    "Please contact the supervising clinician or charge nurse to authorise an " +
                    "over-capacity walk-in or redirect the patient to an urgent-care facility.")),

            WalkInBookingOutcome.PatientNotFound =>
                NotFound(BuildError(
                    StatusCodes.Status404NotFound,
                    "The selected patient record could not be found or has been deactivated.")),

            WalkInBookingOutcome.DuplicatePatientEmail =>
                Conflict(BuildError(
                    StatusCodes.Status409Conflict,
                    "A patient with this email address already exists. Please search for and select the existing record.")),

            _ =>
                StatusCode(StatusCodes.Status500InternalServerError,
                    BuildError(StatusCodes.Status500InternalServerError, "Unexpected booking outcome.")),
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private ErrorResponse BuildError(int statusCode, string message)
    {
        var correlationId = HttpContext.Items[Middleware.CorrelationIdMiddleware.ItemsKey]?.ToString()
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
