using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using UPACIP.Api.Models;
using UPACIP.Service.Auth;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Handles the two-step password reset flow (US_015):
///   POST /api/auth/forgot-password  — initiates the reset (generates token, sends email)
///   POST /api/auth/reset-password   — validates the token and sets the new password
///
/// Both endpoints are public (no [Authorize]) because the user cannot authenticate
/// if they have lost their password.
///
/// Security design:
///   - Anti-enumeration: /forgot-password always returns 200 with the same message
///     regardless of whether the email is registered (OWASP Forgot Password Cheat Sheet).
///   - Rate limiting: /forgot-password is limited to 5 requests per 15 minutes per IP
///     to prevent token-flood abuse (task_002 requirement).
///   - Input is validated by DataAnnotation on the request DTOs; additional password
///     complexity validation is handled by <see cref="IPasswordResetService"/>.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class PasswordResetController : ControllerBase
{
    private readonly IPasswordResetService _passwordResetService;
    private readonly ILogger<PasswordResetController> _logger;

    public PasswordResetController(
        IPasswordResetService passwordResetService,
        ILogger<PasswordResetController> logger)
    {
        _passwordResetService = passwordResetService;
        _logger               = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/forgot-password
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Initiates the password reset flow by sending a reset link to the supplied email.
    /// Always returns 200 OK regardless of whether the email is registered (anti-enumeration).
    /// Rate limited to 5 requests per 15 minutes per IP (abuse prevention).
    /// </summary>
    /// <param name="request">Request body containing the email address.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Reset link sent (or silently skipped for non-registered email).</response>
    /// <response code="400">Request body failed model validation.</response>
    /// <response code="429">Rate limit exceeded — too many requests from this IP.</response>
    [HttpPost("forgot-password")]
    [EnableRateLimiting("forgot-password-limit")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(new ErrorResponse
            {
                StatusCode = 400,
                Message    = "Validation failed.",
                ValidationErrors = ModelState.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value?.Errors.Select(e => e.ErrorMessage).ToArray() ?? []),
            });

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;

        var result = await _passwordResetService.RequestResetAsync(
            request.Email,
            ipAddress,
            cancellationToken);

        // Always return 200 — the message is the same for registered/non-registered (anti-enumeration).
        return Ok(new { message = result.Message });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/reset-password
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates the password reset token and sets the new password.
    /// Returns 410 Gone when the token has expired (AC-3) and 400 Bad Request for invalid tokens.
    /// Returns 422 Unprocessable Entity when the new password fails complexity requirements.
    /// </summary>
    /// <param name="request">Token, email, and new password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">Password reset successfully.</response>
    /// <response code="400">Invalid or malformed request / invalid token.</response>
    /// <response code="410">Reset link has expired — user must request a new one (AC-3).</response>
    /// <response code="422">Password does not meet complexity requirements.</response>
    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status410Gone)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(new ErrorResponse
            {
                StatusCode = 400,
                Message    = "Validation failed.",
                ValidationErrors = ModelState.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value?.Errors.Select(e => e.ErrorMessage).ToArray() ?? []),
            });

        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;

        var result = await _passwordResetService.ResetPasswordAsync(
            request.Email,
            request.Token,
            request.NewPassword,
            ipAddress,
            cancellationToken);

        return result.Outcome switch
        {
            ResetPasswordOutcome.Success => Ok(new { message = result.Message }),

            ResetPasswordOutcome.ExpiredToken => StatusCode(
                StatusCodes.Status410Gone,
                new ErrorResponse
                {
                    StatusCode = 410,
                    Message    = result.Message,
                }),

            ResetPasswordOutcome.PasswordComplexityFailed => UnprocessableEntity(
                new ErrorResponse
                {
                    StatusCode        = 422,
                    Message           = result.Message,
                    ValidationErrors  = result.ValidationErrors,
                }),

            // InvalidToken, InvalidUser — generic 400 (never reveals user existence)
            _ => BadRequest(new ErrorResponse
            {
                StatusCode = 400,
                Message    = result.Message,
            }),
        };
    }
}
