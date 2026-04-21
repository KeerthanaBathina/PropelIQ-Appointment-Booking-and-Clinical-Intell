using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UPACIP.Api.Authorization;
using UPACIP.Api.Models;
using UPACIP.Service.Appointments;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Appointment slot availability endpoints (US_017).
///
/// Endpoint:
///   GET /api/appointments/slots
///     Accepts <see cref="SlotQueryParameters"/> as query string.
///     Returns <see cref="SlotAvailabilityResponse"/> with a flat slot list, provider summary,
///     and per-date availability counts for calendar dot indicators.
///
/// Authorization: Patient or Staff — both roles may view available slots (FR-011).
///
/// Performance targets:
///   AC-1 / NFR-001: ≤ 2 seconds at P95.
///   AC-4 / NFR-030: Sub-second from Redis on cache hit (5-minute TTL).
///
/// Validation:
///   FluentValidation (<see cref="UPACIP.Service.Validation.SlotQueryParametersValidator"/>)
///   runs automatically before the action executes via FluentValidation.AspNetCore.
///   Invalid requests receive 400 with <see cref="ErrorResponse"/>.
///
/// Logging (NFR-035): Correlation ID injected by <c>CorrelationIdMiddleware</c>;
///   all log events include it automatically via the structured logging context.
/// </summary>
[ApiController]
[Route("api/appointments")]
[Authorize(Policy = RbacPolicies.AnyAuthenticated)]
public sealed class AppointmentSlotsController : ControllerBase
{
    private readonly IAppointmentSlotService        _slotService;
    private readonly ILogger<AppointmentSlotsController> _logger;

    public AppointmentSlotsController(
        IAppointmentSlotService             slotService,
        ILogger<AppointmentSlotsController> logger)
    {
        _slotService = slotService;
        _logger      = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/appointments/slots
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns available 30-minute appointment slots for a date range with optional provider/type filters.
    /// </summary>
    /// <remarks>
    /// **Filter rules (FR-013, EC-2):**
    /// - <c>startDate</c>: required; must be today or future (ISO-8601 format YYYY-MM-DD).
    /// - <c>endDate</c>: optional; defaults to startDate; must be ≤ today + 90 days.
    /// - <c>providerId</c>: optional; when omitted, all providers' slots are returned.
    /// - <c>appointmentType</c>: optional; when omitted, all types are included.
    ///
    /// **Response:**
    /// - `slots`: flat list of all slot items (available and unavailable).
    /// - `providers`: distinct provider list for the filter dropdown.
    /// - `dateSummary`: per-date count of available slots (used by calendar dot indicators, AC-2).
    ///
    /// **Caching:** Results are cached in Redis for 5 minutes (NFR-030).
    /// </remarks>
    /// <param name="parameters">Query parameters (bound from query string).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>200 with slot availability, 400 for invalid parameters, 401 if unauthenticated.</returns>
    [HttpGet("slots")]
    [ProducesResponseType(typeof(SlotAvailabilityResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSlots(
        [FromQuery] SlotQueryParameters parameters,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                     ?? User.Identity?.Name
                     ?? "anonymous";

        _logger.LogInformation(
            "Slot availability request by user {UserId}: start={StartDate}, end={EndDate}, " +
            "provider={ProviderId}, type={AppointmentType}.",
            userId,
            parameters.StartDate,
            parameters.ResolvedEndDate,
            parameters.ProviderId?.ToString() ?? "all",
            parameters.AppointmentType ?? "all");

        var response = await _slotService.GetAvailableSlotsAsync(parameters, cancellationToken);

        _logger.LogInformation(
            "Slot availability response for user {UserId}: {SlotCount} slots, {ProviderCount} providers.",
            userId, response.Slots.Count, response.Providers.Count);

        return Ok(response);
    }
}
