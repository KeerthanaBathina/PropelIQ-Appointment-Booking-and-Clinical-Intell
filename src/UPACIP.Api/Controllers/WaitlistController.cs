using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using UPACIP.Api.Authorization;
using UPACIP.Api.Models;
using UPACIP.Service.Appointments;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Waitlist registration, claim-offer redemption, and removal endpoints (US_020).
///
/// Endpoints:
///   POST   /api/waitlist                   — register for the waitlist (AC-1).
///   GET    /api/waitlist/claim/{token}     — redeem a waitlist offer / acquire hold (AC-3).
///   DELETE /api/waitlist/{id}              — remove an active waitlist entry (EC-1).
///
/// Authorization: Patient role only.
///   PatientId is always resolved server-side from the JWT email claim (OWASP A01).
///   Claim tokens are validated for ownership before the hold is acquired.
/// </summary>
[ApiController]
[Route("api/waitlist")]
[Authorize(Policy = RbacPolicies.PatientOnly)]
public sealed class WaitlistController : ControllerBase
{
    private readonly IWaitlistService               _waitlistService;
    private readonly ILogger<WaitlistController>    _logger;

    public WaitlistController(
        IWaitlistService            waitlistService,
        ILogger<WaitlistController> logger)
    {
        _waitlistService = waitlistService;
        _logger          = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/waitlist — register for the waitlist (AC-1)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers the authenticated patient on the waitlist with their preferred date/time/provider.
    /// Returns 201 Created with the persisted entry details, or 409 Conflict when an identical
    /// Active entry already exists for the same patient and criteria.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(JoinWaitlistResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Register(
        [FromBody] JoinWaitlistRequest  request,
        CancellationToken               cancellationToken)
    {
        var userEmail = GetUserEmail();
        if (userEmail is null)
        {
            _logger.LogWarning("WaitlistController.Register: no email claim in JWT.");
            return Unauthorized();
        }

        var result = await _waitlistService.RegisterAsync(userEmail, request, cancellationToken);

        if (result is null)
        {
            return Conflict(BuildError(
                StatusCodes.Status409Conflict,
                "An active waitlist entry with the same criteria already exists."));
        }

        return Created($"/api/waitlist/{result.WaitlistId}", result);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/waitlist/claim/{token} — redeem claim link (AC-3)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Redeems a waitlist offer claim token (from the notification email link), acquires a
    /// 60-second slot hold, and returns the held slot details for the booking UI.
    ///
    /// Status codes:
    ///   200 OK        — hold acquired, booking can proceed.
    ///   404 Not Found — token not found or does not belong to this patient.
    ///   410 Gone      — token has expired (60-second TTL elapsed).
    ///   409 Conflict  — offer was already claimed (idempotent re-entry).
    /// </summary>
    [HttpGet("claim/{token}")]
    [ProducesResponseType(typeof(ClaimWaitlistOfferResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ClaimOffer(
        [FromRoute] string  token,
        CancellationToken   cancellationToken)
    {
        var userEmail = GetUserEmail();
        if (userEmail is null)
        {
            _logger.LogWarning("WaitlistController.ClaimOffer: no email claim in JWT.");
            return Unauthorized();
        }

        var result = await _waitlistService.ClaimOfferAsync(token, userEmail, cancellationToken);

        return result.Status switch
        {
            ClaimWaitlistOfferStatus.Success
                => Ok(result.Response),

            ClaimWaitlistOfferStatus.NotFound
                => NotFound(BuildError(
                    StatusCodes.Status404NotFound,
                    "The waitlist offer was not found or does not belong to your account.")),

            ClaimWaitlistOfferStatus.Expired
                => StatusCode(
                    StatusCodes.Status410Gone,
                    BuildError(
                        StatusCodes.Status410Gone,
                        "This waitlist offer has expired. You have been re-queued for the next available slot.")),

            ClaimWaitlistOfferStatus.AlreadyClaimed
                => Conflict(BuildError(
                    StatusCodes.Status409Conflict,
                    "This offer has already been claimed.")),

            _ => StatusCode(
                    StatusCodes.Status500InternalServerError,
                    BuildError(StatusCodes.Status500InternalServerError, "Unexpected error."))
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DELETE /api/waitlist/{id} — remove a waitlist entry (EC-1)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Removes the specified waitlist entry for the authenticated patient.
    /// Returns 204 No Content on success, 404 Not Found when the entry does not exist
    /// or does not belong to the patient (identical response prevents IDOR enumeration).
    /// </summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RemoveEntry(
        [FromRoute] Guid  id,
        CancellationToken cancellationToken)
    {
        var userEmail = GetUserEmail();
        if (userEmail is null)
        {
            _logger.LogWarning("WaitlistController.RemoveEntry: no email claim in JWT.");
            return Unauthorized();
        }

        var removed = await _waitlistService.RemoveEntryAsync(id, userEmail, cancellationToken);

        if (!removed)
        {
            return NotFound(BuildError(
                StatusCodes.Status404NotFound,
                "Waitlist entry not found."));
        }

        return NoContent();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private string? GetUserEmail()
        => User.FindFirst(JwtRegisteredClaimNames.Email)?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;

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
