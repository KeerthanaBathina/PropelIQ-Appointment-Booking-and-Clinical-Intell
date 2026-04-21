using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UPACIP.Api.Authorization;
using UPACIP.Api.Models;
using UPACIP.Service.Appointments;

namespace UPACIP.Api.Controllers;

/// Appointment booking, cancellation, and rescheduling endpoints (US_018, US_019, US_023).
///
/// Endpoints:
///   GET    /api/appointments                              — list patient's own appointments (US_019).
///   POST   /api/appointments                             — confirm a booking (requires a prior hold).
///   POST   /api/appointments/hold                        — acquire a 60-second slot hold.
///   DELETE /api/appointments/hold/{slotId}               — release a slot hold.
///   DELETE /api/appointments/{appointmentId}             — cancel a scheduled appointment (US_019).
///   PUT    /api/appointments/{appointmentId}/reschedule  — reschedule to a new slot (US_023).
///
/// Authorization: Patient role only (FR-012).
///   All endpoints require a valid JWT with the Patient role.
///   PatientId is always resolved server-side from the JWT email claim — never trusted
///   from the request body (OWASP A01 — Broken Access Control prevention).
///
/// Concurrency (FR-012, TR-015):
///   Booking uses optimistic locking (EF Core Version concurrency token).
///   On conflict the endpoint returns 409 with up to 3 alternative slots (AC-2).
///
/// Performance (NFR-001):
///   Target ≤ 2 seconds P95 for POST /api/appointments.
///
/// Logging (NFR-035):
///   CorrelationId injected by <c>CorrelationIdMiddleware</c>; all log events include it
///   automatically via the Serilog structured logging context.
/// </summary>
[ApiController]
[Route("api/appointments")]
[Authorize(Policy = RbacPolicies.PatientOnly)]
public sealed class AppointmentBookingController : ControllerBase
{
    private readonly IAppointmentBookingService          _bookingService;
    private readonly ISlotHoldService                    _holdService;
    private readonly IAppointmentCancellationService     _cancellationService;
    private readonly IAppointmentReschedulingService     _reschedulingService;
    private readonly IAppointmentHistoryService          _historyService;
    private readonly ILogger<AppointmentBookingController> _logger;

    public AppointmentBookingController(
        IAppointmentBookingService              bookingService,
        ISlotHoldService                        holdService,
        IAppointmentCancellationService         cancellationService,
        IAppointmentReschedulingService         reschedulingService,
        IAppointmentHistoryService              historyService,
        ILogger<AppointmentBookingController>   logger)
    {
        _bookingService      = bookingService;
        _holdService         = holdService;
        _cancellationService = cancellationService;
        _reschedulingService = reschedulingService;
        _historyService      = historyService;
        _logger              = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/appointments/history — paginated appointment history (US_024)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the authenticated patient's paginated, sortable appointment history (US_024, FR-024).
    /// All statuses are included — Scheduled, Completed, Cancelled, NoShow — so cancelled rows
    /// remain visible with a grey badge in the SCR-007 table (EC-2).
    /// </summary>
    /// <remarks>
    /// **Ownership (OWASP A01):** Patient is resolved from the JWT email claim.
    ///   PatientId is never accepted from the query string.
    ///
    /// **Pagination (AC-3):** Fixed page size of 10. Pass <c>page</c> to navigate.
    ///   Total count and total pages are always returned in the response.
    ///
    /// **Sort (AC-1, AC-2):** Default is <c>desc</c> (newest-first).
    ///   Pass <c>sortDirection=asc</c> to reverse the order.
    ///
    /// **Empty state (EC-1):** Returns 200 with an empty <c>items</c> array and zero counts
    ///   rather than 404 when the patient has no appointment history.
    ///
    /// **Performance (NFR-004):** EF Core projection fetches only the columns required by SCR-007.
    /// </remarks>
    /// <param name="query">Pagination and sort parameters (page, sortDirection).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>200 OK with <see cref="AppointmentHistoryResponse"/>.</returns>
    [HttpGet("history")]
    [ProducesResponseType(typeof(AppointmentHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAppointmentHistory(
        [FromQuery] AppointmentHistoryQuery query,
        CancellationToken                   cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(BuildError(
                StatusCodes.Status400BadRequest,
                "Invalid pagination parameters."));
        }

        var userEmail = GetUserEmail();
        if (userEmail is null)
        {
            _logger.LogWarning("GetAppointmentHistory: no email claim in JWT.");
            return Unauthorized();
        }

        _logger.LogInformation(
            "History request: page={Page}, sort={Sort}.",
            query.Page, query.SortDirection);

        var response = await _historyService.GetHistoryAsync(
            userEmail, query, cancellationToken);

        return Ok(response);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/appointments — patient appointment list (US_019)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all appointments for the authenticated patient, ordered most-recent first.
    /// The <c>cancellable</c> flag in each item is evaluated in UTC by the server — the client
    /// must not recompute cancellation eligibility (EC-2).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>200 OK with an array of <see cref="PatientAppointmentSummary"/>.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PatientAppointmentSummary>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetAppointments(CancellationToken cancellationToken)
    {
        var userEmail = GetUserEmail();
        if (userEmail is null)
        {
            _logger.LogWarning("GetAppointments: no email claim in JWT.");
            return Unauthorized();
        }

        var appointments = await _cancellationService
            .GetPatientAppointmentsAsync(userEmail, cancellationToken);

        return Ok(appointments);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/appointments — confirm booking
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Confirms an appointment booking for the authenticated patient.
    /// Requires an active slot hold (POST /api/appointments/hold) before calling this endpoint.
    /// </summary>
    /// <remarks>
    /// **Pre-conditions:**
    /// - Patient must have acquired a slot hold via POST /api/appointments/hold.
    /// - Hold TTL is 60 seconds. If expired the patient must re-acquire before re-submitting.
    /// - AppointmentTime must be between now and today + 90 days (FR-013, EC-2).
    ///
    /// **Success (201):** Returns <see cref="BookingResponse"/> with booking reference number (AC-4).
    ///
    /// **Conflict (409):** Slot taken by another booking — returns <see cref="ConflictBookingResponse"/>
    ///   with up to 3 alternative available slots (AC-2).
    ///
    /// **Unprocessable (422):** Hold not found or expired — acquire a new hold first (AC-3).
    ///
    /// **Service Unavailable (503):** Transient database failure after retry — retry later (EC-1).
    /// </remarks>
    /// <param name="request">Booking request (SlotId, ProviderId, AppointmentTime, AppointmentType).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>201 Created with <see cref="BookingResponse"/>, or an error response.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(BookingResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ConflictBookingResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status503ServiceUnavailable)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> BookAppointment(
        [FromBody] BookingRequest request,
        CancellationToken         cancellationToken)
    {
        var userEmail = GetUserEmail();
        if (userEmail is null)
        {
            _logger.LogWarning("Booking attempt with no email claim in JWT.");
            return Unauthorized();
        }

        _logger.LogInformation(
            "Booking attempt: slot={SlotId}, provider={ProviderId}, time={AppointmentTime}, user={Email}.",
            request.SlotId, request.ProviderId, request.AppointmentTime, userEmail);

        var result = await _bookingService.BookAppointmentAsync(request, userEmail, cancellationToken);

        return result.Status switch
        {
            BookingResultStatus.Success =>
                CreatedAtAction(
                    nameof(BookAppointment),
                    new { id = result.Booking!.AppointmentId },
                    result.Booking),

            BookingResultStatus.Conflict =>
                Conflict(new ConflictBookingResponse(
                    result.ErrorMessage!,
                    result.AlternativeSlots ?? [])),

            BookingResultStatus.HoldNotOwned =>
                UnprocessableEntity(BuildError(
                    StatusCodes.Status422UnprocessableEntity,
                    result.ErrorMessage!)),

            BookingResultStatus.PatientNotFound =>
                UnprocessableEntity(BuildError(
                    StatusCodes.Status422UnprocessableEntity,
                    result.ErrorMessage!)),

            BookingResultStatus.ServiceUnavailable =>
                StatusCode(
                    StatusCodes.Status503ServiceUnavailable,
                    BuildError(StatusCodes.Status503ServiceUnavailable, result.ErrorMessage!)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/appointments/hold — acquire slot hold
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Acquires a 60-second temporary hold on the specified slot for the authenticated patient.
    /// Must be called before POST /api/appointments to reserve the slot during checkout (AC-3).
    /// </summary>
    /// <remarks>
    /// The hold expires automatically after 60 seconds (TTL enforced by Redis).
    /// If the slot is already held by a different patient, 409 Conflict is returned.
    /// </remarks>
    /// <param name="request">Hold request containing the SlotId to reserve.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>200 OK with hold confirmation, or 409 if slot is already held.</returns>
    [HttpPost("hold")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AcquireHold(
        [FromBody] HoldRequest request,
        CancellationToken      cancellationToken)
    {
        var userEmail = GetUserEmail();
        if (userEmail is null)
        {
            _logger.LogWarning("Hold acquisition attempt with no email claim in JWT.");
            return Unauthorized();
        }

        _logger.LogInformation(
            "Hold acquisition attempt: slot={SlotId}, user={Email}.",
            request.SlotId, userEmail);

        var acquired = await _holdService.AcquireHoldAsync(
            request.SlotId, userEmail, cancellationToken);

        if (!acquired)
        {
            return Conflict(BuildError(
                StatusCodes.Status409Conflict,
                "Slot is already held by another patient. Please select a different slot or try again shortly."));
        }

        _logger.LogInformation(
            "Hold acquired: slot={SlotId}, user={Email}, expiresInSeconds=60.",
            request.SlotId, userEmail);

        return Ok(new { slotId = request.SlotId, expiresInSeconds = 60 });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE /api/appointments/hold/{slotId} — release slot hold
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Explicitly releases the slot hold for the authenticated patient.
    /// Holds also expire automatically after 60 seconds (AC-3).
    /// </summary>
    /// <remarks>
    /// This endpoint is idempotent — calling it when no hold exists returns 204 No Content.
    /// Only the patient who acquired the hold can release it.
    /// </remarks>
    /// <param name="slotId">Slot identifier to release.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>204 No Content.</returns>
    [HttpDelete("hold/{slotId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ReleaseHold(
        [FromRoute] string slotId,
        CancellationToken  cancellationToken)
    {
        var userEmail = GetUserEmail();
        if (userEmail is null)
        {
            _logger.LogWarning("Hold release attempt with no email claim in JWT.");
            return Unauthorized();
        }

        await _holdService.ReleaseHoldAsync(slotId, userEmail, cancellationToken);

        _logger.LogInformation(
            "Hold release request: slot={SlotId}, user={Email}.",
            slotId, userEmail);

        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE /api/appointments/{appointmentId} — cancel appointment (US_019)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Cancels a scheduled appointment for the authenticated patient.
    /// Enforces the 24-hour UTC cancellation policy and releases the slot back to inventory.
    /// </summary>
    /// <remarks>
    /// **Ownership (OWASP A01):** The patient is resolved from the JWT email claim.
    ///   An appointment that does not belong to the authenticated patient returns 404.
    ///
    /// **Success (200):** Appointment cancelled; slot released within 1 minute (AC-1, AC-3).
    ///
    /// **Conflict (409):** Appointment was already cancelled — idempotent response (EC-1).
    ///
    /// **Unprocessable (422):** Within the 24-hour cutoff — exact policy message returned (AC-2).
    ///   Message: "Cancellations within 24 hours are not permitted. Please contact the clinic."
    ///
    /// **Not Found (404):** Appointment not found or not owned by the requesting patient (OWASP A01).
    ///
    /// **Timezone (EC-2):** Cutoff evaluated in UTC on the server.
    ///   Appointment times are stored in UTC and displayed in the patient's local timezone by the frontend.
    /// </remarks>
    /// <param name="appointmentId">UUID of the appointment to cancel.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>200 OK with <see cref="CancelAppointmentResponse"/>, or an error response.</returns>
    [HttpDelete("{appointmentId:guid}")]
    [ProducesResponseType(typeof(CancelAppointmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> CancelAppointment(
        [FromRoute] Guid  appointmentId,
        CancellationToken cancellationToken)
    {
        var userEmail = GetUserEmail();
        if (userEmail is null)
        {
            _logger.LogWarning("CancelAppointment: no email claim in JWT.");
            return Unauthorized();
        }

        _logger.LogInformation(
            "Cancellation request: appointmentId={AppointmentId}, user={Email}.",
            appointmentId, userEmail);

        var result = await _cancellationService
            .CancelAppointmentAsync(appointmentId, userEmail, cancellationToken);

        return result.Status switch
        {
            CancellationResultStatus.Success =>
                Ok(new CancelAppointmentResponse(
                    result.AppointmentId!.Value,
                    "Cancelled",
                    "Your appointment has been successfully cancelled.",
                    new DateTimeOffset(result.CancelledAt!.Value, TimeSpan.Zero))),

            CancellationResultStatus.AlreadyCancelled =>
                Conflict(BuildError(StatusCodes.Status409Conflict, result.Message!)),

            CancellationResultStatus.PolicyBlocked =>
                UnprocessableEntity(BuildError(
                    StatusCodes.Status422UnprocessableEntity, result.Message!)),

            CancellationResultStatus.NotFound =>
                NotFound(BuildError(StatusCodes.Status404NotFound, result.Message!)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/appointments/{appointmentId}/reschedule — reschedule (US_023)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Atomically reschedules a patient's scheduled appointment to a new slot.
    /// Releases the original slot and creates the new booking in a single transaction.
    /// </summary>
    /// <remarks>
    /// **Ownership (OWASP A01):** Patient is resolved from the JWT email claim.
    ///   An appointment not owned by the requesting patient returns 404.
    ///
    /// **Success (200):** Returns <see cref="RescheduleAppointmentResponse"/> with both original
    ///   and new appointment times (AC-3). Booking reference is preserved (AC-4).
    ///
    /// **Unprocessable (422):** Reschedule is within 24 hours of the original appointment (AC-2).
    ///   Exact message: "Cannot reschedule within 24 hours of appointment."
    ///
    /// **Forbidden (403):** Walk-in appointments cannot be rescheduled by patients (EC-2).
    ///
    /// **Conflict (409):** Selected replacement slot was taken during confirmation (EC-1).
    ///   Frontend should refresh slot options and allow the patient to re-select.
    ///
    /// **Not Found (404):** Appointment not found or not owned by the requesting patient.
    ///
    /// **Timezone (AC-2):** The 24-hour cutoff is evaluated in UTC on the server against the
    ///   original appointment time. No client-provided timezone is accepted.
    ///
    /// **Performance (AC-1, NFR-001):** Target ≤ 2 seconds P95 — single transactional unit.
    /// </remarks>
    /// <param name="appointmentId">UUID of the appointment to reschedule.</param>
    /// <param name="request">Replacement slot details (SlotId, ProviderId, NewAppointmentTime, AppointmentType).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>200 OK with <see cref="RescheduleAppointmentResponse"/>, or an error response.</returns>
    [HttpPut("{appointmentId:guid}/reschedule")]
    [ProducesResponseType(typeof(RescheduleAppointmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RescheduleAppointment(
        [FromRoute] Guid                         appointmentId,
        [FromBody]  RescheduleAppointmentRequest request,
        CancellationToken                        cancellationToken)
    {
        var userEmail = GetUserEmail();
        if (userEmail is null)
        {
            _logger.LogWarning("RescheduleAppointment: no email claim in JWT.");
            return Unauthorized();
        }

        _logger.LogInformation(
            "Reschedule request: appointmentId={AppointmentId}, newSlot={SlotId}, user={Email}.",
            appointmentId, request.SlotId, userEmail);

        var result = await _reschedulingService
            .RescheduleAppointmentAsync(appointmentId, request, userEmail, cancellationToken);

        return result.Status switch
        {
            RescheduleAppointmentStatus.Succeeded =>
                Ok(new RescheduleAppointmentResponse(
                    AppointmentId:      result.AppointmentId!.Value,
                    BookingReference:   result.BookingReference,
                    OldAppointmentTime: new DateTimeOffset(result.OldAppointmentTime!.Value, TimeSpan.Zero),
                    NewAppointmentTime: new DateTimeOffset(result.NewAppointmentTime!.Value, TimeSpan.Zero),
                    ProviderName:       result.ProviderName!,
                    AppointmentType:    result.AppointmentType!,
                    Message:            "Your appointment has been rescheduled.")),

            RescheduleAppointmentStatus.PolicyBlocked =>
                UnprocessableEntity(BuildError(
                    StatusCodes.Status422UnprocessableEntity, result.Message!)),

            RescheduleAppointmentStatus.WalkInRestricted =>
                StatusCode(StatusCodes.Status403Forbidden,
                    BuildError(StatusCodes.Status403Forbidden, result.Message!)),

            RescheduleAppointmentStatus.SlotUnavailable =>
                Conflict(BuildError(StatusCodes.Status409Conflict, result.Message!)),

            RescheduleAppointmentStatus.NotFound =>
                NotFound(BuildError(StatusCodes.Status404NotFound, result.Message!)),

            _ => StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the email claim from the authenticated user's JWT.
    /// Returns <c>null</c> when the claim is absent (should never happen for a valid JWT).
    /// </summary>
    private string? GetUserEmail()
        => User.FindFirst(JwtRegisteredClaimNames.Email)?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;

    /// <summary>Builds a structured <see cref="ErrorResponse"/> for non-success results.</summary>
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
