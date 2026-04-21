using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using UPACIP.Api.Authorization;
using UPACIP.DataAccess.Entities;
using UPACIP.Service.Auth;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Session management endpoints — extend active session and query session status.
/// All endpoints require a valid JWT (AnyAuthenticated policy).
/// </summary>
[ApiController]
[Route("api/session")]
[Authorize(Policy = RbacPolicies.AnyAuthenticated)]
public sealed class SessionController : ControllerBase
{
    private const string SessionExpiredMessage = "Session expired. Please log in again.";
    private readonly ISessionService _sessionService;
    private readonly ITokenService _tokenService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly JwtSettings _jwtSettings;
    private readonly ILogger<SessionController> _logger;

    public SessionController(
        ISessionService sessionService,
        ITokenService tokenService,
        UserManager<ApplicationUser> userManager,
        JwtSettings jwtSettings,
        ILogger<SessionController> logger)
    {
        _sessionService = sessionService;
        _tokenService   = tokenService;
        _userManager    = userManager;
        _jwtSettings    = jwtSettings;
        _logger         = logger;
    }

    // ────────────────────────────────────────────────────────────────────────────
    // POST /api/session/extend
    // ────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Resets the 15-minute inactivity TTL and issues a new JWT access token (AC-4).
    /// Called by the frontend warning modal with ~2 minutes remaining before expiry.
    ///
    /// Returns 200 with a fresh access token and <c>expiresAt</c> (UTC ISO-8601).
    /// Returns 401 when the Redis session has already expired — client must re-login.
    /// </summary>
    [HttpPost("extend")]
    public async Task<IActionResult> Extend(CancellationToken cancellationToken)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { message = SessionExpiredMessage });

        bool isActive;
        try
        {
            isActive = await _sessionService.IsSessionActiveAsync(userId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "SessionController.Extend: Redis unavailable for user {UserId}. Denying extend.",
                userId);
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { message = "Session service temporarily unavailable. Please try again." });
        }

        if (!isActive)
        {
            _logger.LogInformation(
                "Session extend rejected — session already expired for user {UserId}.", userId);
            return Unauthorized(new { message = SessionExpiredMessage });
        }

        // Look up full user details to re-issue a properly-scoped access token.
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null || user.DeletedAt is not null)
            return Unauthorized(new { message = SessionExpiredMessage });

        var roles          = await _userManager.GetRolesAsync(user);
        var newAccessToken = _tokenService.GenerateAccessToken(user, roles);
        var expiresAt      = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpiryMinutes);

        // Reset the Redis sliding TTL (AC-2 / AC-4 — timer resets on extend).
        try
        {
            await _sessionService.UpdateActivityAsync(userId, cancellationToken);
        }
        catch (Exception ex)
        {
            // Non-fatal: new token was issued; log Redis failure.
            _logger.LogWarning(
                ex,
                "SessionController.Extend: Redis activity update failed for user {UserId}.",
                userId);
        }

        _logger.LogInformation(
            "Session extended for user {UserId}. New token expires at {ExpiresAt}.", userId, expiresAt);

        return Ok(new ExtendSessionResponse(newAccessToken, expiresAt));
    }

    // ────────────────────────────────────────────────────────────────────────────
    // GET /api/session/status
    // ────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Returns whether the caller has an active session in Redis.
    /// Useful for the frontend to determine whether to display a session warning.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        var userId2 = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId2))
            return Unauthorized(new { message = SessionExpiredMessage });

        SessionData? session = null;
        try
        {
            session = await _sessionService.GetSessionAsync(userId2, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "SessionController.Status: Redis unavailable for user {UserId}.",
                userId2);
        }

        if (session is null)
            return Ok(new { active = false });

        return Ok(new
        {
            active       = true,
            lastActivity = session.LastActivity,
            loginAt      = session.LoginAt,
        });
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// Response DTOs
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>Response body for a successful session extend.</summary>
public sealed record ExtendSessionResponse(
    string AccessToken,
    DateTime ExpiresAt,
    string TokenType = "Bearer");
