using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using UPACIP.Api.Authorization;
using UPACIP.Api.Models;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Auth;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Authentication endpoints — login, MFA, refresh, logout, register, and email verification.
///
/// Security design:
///   - Refresh tokens via HttpOnly Secure SameSite=Strict cookies (OWASP HttpOnly protection).
///   - Login pipeline: lockout-check → credential-validation → MFA-check → JWT-issue (US_016).
///   - Anti-enumeration: identical response shape for unknown email and wrong password (OWASP A07).
///   - Account lockout: 5 failed attempts → 30-minute lock (NFR-016). Returns 423 + lockedUntil.
///   - MFA tokens: short-lived JWTs (5 min) with purpose=mfa-verification; no role claims.
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private const string RefreshTokenCookieName = "refreshToken";
    private const string SessionExpiredMessage  = "Session expired. Please log in again.";

    private readonly UserManager<ApplicationUser>   _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ITokenService                  _tokenService;
    private readonly JwtSettings                    _jwtSettings;
    private readonly IRegistrationService           _registrationService;
    private readonly ConcurrentSessionGuard         _sessionGuard;
    private readonly IMfaService                    _mfaService;
    private readonly IAuditLogService               _auditLogService;
    private readonly ILogger<AuthController>        _logger;

    public AuthController(
        UserManager<ApplicationUser>   userManager,
        SignInManager<ApplicationUser> signInManager,
        ITokenService                  tokenService,
        JwtSettings                    jwtSettings,
        IRegistrationService           registrationService,
        ConcurrentSessionGuard         sessionGuard,
        IMfaService                    mfaService,
        IAuditLogService               auditLogService,
        ILogger<AuthController>        logger)
    {
        _userManager         = userManager;
        _signInManager       = signInManager;
        _tokenService        = tokenService;
        _jwtSettings         = jwtSettings;
        _registrationService = registrationService;
        _sessionGuard        = sessionGuard;
        _mfaService          = mfaService;
        _auditLogService     = auditLogService;
        _logger              = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/login
    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Login pipeline (US_016):
    /// (a) Lockout pre-check → 423 + lockedUntil if currently locked.
    /// (b) Credential validation → 401 + remainingAttempts on failure; 423 on lock triggered.
    /// (c) MFA check → 200 + { mfaRequired: true, mfaToken } when MFA is enabled.
    /// (d) Full JWT + refresh token + last-login update → 200 + AuthSuccessResponse when MFA is off.
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var ipAddress = GetClientIpAddress();
        var userAgent = Request.Headers.UserAgent.ToString();

        // (a) Lookup — same generic error for unknown email as for wrong password (OWASP A07).
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null || user.DeletedAt is not null)
            return Unauthorized(new { message = "Invalid credentials." });

        // (a) Pre-check lockout: prevents counter reset on a locked-out account (edge case AC-2).
        if (await _userManager.IsLockedOutAsync(user))
        {
            var lockedUntil = user.LockoutEnd?.UtcDateTime;
            _logger.LogWarning("Login rejected \u2014 user {UserId} is locked until {LockedUntil}.", user.Id, lockedUntil);
            return StatusCode(423, new { message = "Account locked. Please try again later.", lockedUntil });
        }

        // (b) Credential validation \u2014 Identity handles AccessFailedCount + lockout transitions.
        var result = await _signInManager.CheckPasswordSignInAsync(
            user, request.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            var freshUser   = await _userManager.FindByIdAsync(user.Id.ToString());
            var lockedUntil = freshUser?.LockoutEnd?.UtcDateTime;
            _logger.LogWarning("Account locked for user {UserId} after failed attempt.", user.Id);
            await _auditLogService.LogAsync(AuditAction.AccountLocked, user.Id, "User",
                ipAddress, userAgent, cancellationToken: cancellationToken);
            return StatusCode(423, new { message = "Account locked. Please try again later.", lockedUntil });
        }

        if (!result.Succeeded)
        {
            var freshUser         = await _userManager.FindByIdAsync(user.Id.ToString());
            var maxAttempts       = _userManager.Options.Lockout.MaxFailedAccessAttempts;
            var remainingAttempts = Math.Max(0, maxAttempts - (freshUser?.AccessFailedCount ?? 0));
            await _auditLogService.LogAsync(AuditAction.FailedLogin, user.Id, "User",
                ipAddress, userAgent, cancellationToken: cancellationToken);
            return Unauthorized(new { message = "Invalid credentials.", remainingAttempts });
        }

        // (c) MFA check — if MFA is enabled, issue short-lived mfa token; do not issue full JWT yet.
        if (user.MfaEnabled)
        {
            var mfaToken = _tokenService.GenerateMfaToken(user);
            _logger.LogInformation("MFA required for user {UserId}.", user.Id);
            return Ok(new { mfaRequired = true, mfaToken });
        }

        // (d) No MFA — issue full tokens + update last-login.
        return await IssueFullTokensAsync(user, ipAddress, userAgent, cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/mfa/verify   (login MFA step)
    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Validates a TOTP code (6 digits) or backup code (8 chars) against the user identified
    /// by the 5-minute mfaToken. Issues full JWT + refresh token on success (US_016 AC-1).
    /// Rate-limited to 5 attempts per minute per IP to prevent brute-force.
    /// </summary>
    [HttpPost("mfa/verify")]
    [AllowAnonymous]
    [EnableRateLimiting("mfa-verify-limit")]
    public async Task<IActionResult> MfaVerify(
        [FromBody] MfaVerifyRequest request, CancellationToken cancellationToken)
    {
        var ipAddress = GetClientIpAddress();
        var userAgent = Request.Headers.UserAgent.ToString();

        var principal = _tokenService.ValidateMfaToken(request.MfaToken);
        if (principal is null)
            return Unauthorized(new { message = "MFA token is invalid or expired." });

        var userIdValue = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdValue, out var userId))
            return Unauthorized(new { message = "MFA token is invalid or expired." });

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || user.DeletedAt is not null || !user.MfaEnabled)
            return Unauthorized(new { message = "MFA token is invalid or expired." });

        // Try TOTP (6 digits) first; fall back to backup code (8 chars).
        var code = request.Code.Trim();
        var verified = code.Length == 6
            ? await _mfaService.VerifyTotpCodeAsync(userId, code, cancellationToken)
            : await _mfaService.VerifyBackupCodeAsync(userId, code, cancellationToken);

        if (!verified)
        {
            _logger.LogWarning("MFA verification failed for user {UserId}.", userId);
            return Unauthorized(new { message = "Invalid MFA code." });
        }

        await _auditLogService.LogAsync(AuditAction.MfaVerified, userId, "User",
            ipAddress, userAgent, cancellationToken: cancellationToken);

        return await IssueFullTokensAsync(user, ipAddress, userAgent, cancellationToken);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/mfa/setup
    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Generates a TOTP secret and returns the OTP auth URL + manual-entry key for QR display.
    /// The secret is stored encrypted; MFA is NOT yet active until verify-setup succeeds.
    /// </summary>
    [HttpPost("mfa/setup")]
    [Authorize(Policy = RbacPolicies.StaffOrAdmin)]
    public async Task<IActionResult> MfaSetup(CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdValue, out var userId))
            return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || user.DeletedAt is not null)
            return Unauthorized();

        var setupData = await _mfaService.GenerateSecretAsync(userId, user.Email!, cancellationToken);
        return Ok(new MfaSetupResponse(setupData.OtpAuthUrl, setupData.ManualEntryKey));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/mfa/verify-setup
    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Verifies the TOTP code against the pending secret, activates MFA, and returns
    /// 8 one-time backup codes (shown exactly once). Logs MfaEnabled to audit trail.
    /// </summary>
    [HttpPost("mfa/verify-setup")]
    [Authorize(Policy = RbacPolicies.StaffOrAdmin)]
    public async Task<IActionResult> MfaVerifySetup(
        [FromBody] MfaVerifySetupRequest request, CancellationToken cancellationToken)
    {
        var ipAddress = GetClientIpAddress();
        var userAgent = Request.Headers.UserAgent.ToString();

        var userIdValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdValue, out var userId))
            return Unauthorized();

        var backupCodes = await _mfaService.EnableMfaAsync(userId, request.Code, cancellationToken);
        if (backupCodes is null)
            return BadRequest(new { message = "Invalid verification code. Scan the QR code again and retry." });

        await _auditLogService.LogAsync(AuditAction.MfaEnabled, userId, "User",
            ipAddress, userAgent, cancellationToken: cancellationToken);

        _logger.LogInformation("MFA enabled for user {UserId}.", userId);
        return Ok(new { message = "MFA enabled successfully.", backupCodes });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/auth/mfa/disable
    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Disables MFA for the authenticated user after password confirmation.
    /// Clears the TOTP secret and backup codes from the user record.
    /// </summary>
    [HttpPost("mfa/disable")]
    [Authorize(Policy = RbacPolicies.AnyAuthenticated)]
    public async Task<IActionResult> MfaDisable(
        [FromBody] MfaDisableRequest request, CancellationToken cancellationToken)
    {
        var ipAddress = GetClientIpAddress();
        var userAgent = Request.Headers.UserAgent.ToString();

        var userIdValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdValue, out var userId))
            return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || user.DeletedAt is not null)
            return Unauthorized();

        if (!await _userManager.CheckPasswordAsync(user, request.Password))
            return Unauthorized(new { message = "Password confirmation failed." });

        await _mfaService.DisableMfaAsync(userId, cancellationToken);
        await _auditLogService.LogAsync(AuditAction.MfaDisabled, userId, "User",
            ipAddress, userAgent, cancellationToken: cancellationToken);

        _logger.LogInformation("MFA disabled for user {UserId}.", userId);
        return Ok(new { message = "MFA has been disabled." });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/admin/mfa/reset/{targetUserId}
    // ─────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Admin-only: resets (disables) MFA for any specified user. The action is written
    /// to the immutable audit trail with the admin's ID and the target user's ID.
    /// </summary>
    [HttpPost("~/api/admin/mfa/reset/{targetUserId:guid}")]
    [Authorize(Policy = RbacPolicies.AdminOnly)]
    public async Task<IActionResult> AdminMfaReset(
        Guid targetUserId, CancellationToken cancellationToken)
    {
        var ipAddress = GetClientIpAddress();
        var userAgent = Request.Headers.UserAgent.ToString();

        var adminIdValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(adminIdValue, out var adminId))
            return Unauthorized();

        var targetUser = await _userManager.FindByIdAsync(targetUserId.ToString());
        if (targetUser is null || targetUser.DeletedAt is not null)
            return NotFound(new { message = "User not found." });

        await _mfaService.DisableMfaAsync(targetUserId, cancellationToken);

        await _auditLogService.LogAsync(AuditAction.AdminMfaReset, adminId, "User",
            ipAddress, userAgent, resourceId: targetUserId, cancellationToken: cancellationToken);

        _logger.LogWarning("Admin {AdminId} reset MFA for user {TargetUserId}.", adminId, targetUserId);
        return Ok(new { message = $"MFA has been reset for user {targetUserId}." });
    }
    /// <summary>
    /// Issues a new access + refresh token pair from a valid (non-blacklisted) refresh token
    /// cookie and the caller's expired access token. The consumed refresh token is immediately
    /// blacklisted (token rotation — AC-3, AC-4).
    /// Returns 401 with "Session expired. Please log in again." for any invalid state.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshRequest request, CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies[RefreshTokenCookieName];
        if (string.IsNullOrWhiteSpace(refreshToken))
            return Unauthorized(new { message = SessionExpiredMessage });

        if (await _tokenService.IsRefreshTokenBlacklistedAsync(refreshToken, cancellationToken))
            return Unauthorized(new { message = SessionExpiredMessage });

        var principal = _tokenService.GetPrincipalFromExpiredToken(request.AccessToken);
        if (principal is null)
            return Unauthorized(new { message = SessionExpiredMessage });

        // The sub claim contains the user's GUID (set in TokenService.GenerateAccessToken).
        var userIdValue = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdValue, out var userId))
            return Unauthorized(new { message = SessionExpiredMessage });

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || user.DeletedAt is not null)
            return Unauthorized(new { message = SessionExpiredMessage });

        // Blacklist the consumed refresh token BEFORE issuing the new pair
        // so a race-condition replay cannot use the same token twice.
        await _tokenService.BlacklistRefreshTokenAsync(refreshToken, cancellationToken);

        var roles           = await _userManager.GetRolesAsync(user);
        var newAccessToken  = _tokenService.GenerateAccessToken(user, roles);
        var newRefreshToken = _tokenService.GenerateRefreshToken();

        AppendRefreshTokenCookie(newRefreshToken);

        return Ok(new TokenResponse(newAccessToken));
    }

    // ────────────────────────────────────────────────────────────────────────────
    // POST /api/auth/logout
    // ────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Blacklists the current refresh token in Redis and clears the cookie (AC-4).
    /// Requires a valid access token so unauthenticated callers cannot spray the blacklist.
    /// </summary>
    [HttpPost("logout")]
    [Authorize(Policy = RbacPolicies.AnyAuthenticated)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var ipAddress = GetClientIpAddress();
        var userAgent = Request.Headers.UserAgent.ToString();

        var refreshToken = Request.Cookies[RefreshTokenCookieName];
        if (!string.IsNullOrWhiteSpace(refreshToken))
            await _tokenService.BlacklistRefreshTokenAsync(refreshToken, cancellationToken);

        // Blacklist the current JWT's jti so it cannot be reused after logout (AC-1, task_002 step 5/6).
        var jti      = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        var expValue = User.FindFirst(JwtRegisteredClaimNames.Exp)?.Value;
        if (!string.IsNullOrEmpty(jti))
        {
            var remaining = TimeSpan.FromMinutes(_jwtSettings.AccessTokenExpiryMinutes);
            if (!string.IsNullOrEmpty(expValue) && long.TryParse(expValue, out var expUnix))
            {
                var naturalExpiry = DateTimeOffset.FromUnixTimeSeconds(expUnix);
                var computed      = naturalExpiry - DateTimeOffset.UtcNow;
                if (computed > TimeSpan.Zero)
                    remaining = computed;
            }
            await _tokenService.BlacklistJtiAsync(jti, remaining, cancellationToken);
        }

        var userIdValue = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(userIdValue, out var userId))
        {
            await _sessionGuard.InvalidateAsync(userId.ToString(), cancellationToken);
            await _auditLogService.LogAsync(AuditAction.Logout, userId, "User",
                ipAddress, userAgent, cancellationToken: cancellationToken);
        }

        Response.Cookies.Delete(RefreshTokenCookieName);
        _logger.LogInformation("User {UserId} logged out.", userIdValue);
        return NoContent();
    }

    // ────────────────────────────────────────────────────────────────────────────
    // POST /api/auth/register
    // ────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Creates a new patient account with status "pending verification" and dispatches
    /// a verification email within 2 minutes (AC-1). Returns 409 for duplicate email
    /// without revealing verification status (AC-4). Returns 400 with specific criteria
    /// when password complexity is not satisfied (AC-5).
    /// </summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting("register-limit")]
    public async Task<IActionResult> Register(
        [FromBody] RegistrationRequest request, CancellationToken cancellationToken)
    {
        var ipAddress = GetClientIpAddress();
        var result    = await _registrationService.RegisterAsync(request, ipAddress, cancellationToken);

        if (!result.Success)
        {
            // Duplicate email → 409 Conflict
            if (result.ValidationErrors is null)
                return Conflict(new { message = result.Message });

            // Password / field validation errors → 400 Bad Request with per-field details
            return BadRequest(new { message = result.Message, validationErrors = result.ValidationErrors });
        }

        return Ok(new { message = result.Message });
    }

    // ────────────────────────────────────────────────────────────────────────────
    // POST /api/auth/verify-email
    // ────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Confirms an email verification token.
    /// 200 → account activated (AC-2).
    /// 410 Gone → link expired (1-hour expiry) — client should resend (AC-3).
    /// 400 Bad Request → invalid or already-used token.
    /// </summary>
    [HttpPost("verify-email")]
    [AllowAnonymous]
    public async Task<IActionResult> VerifyEmail(
        [FromBody] VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        var result = await _registrationService.VerifyEmailAsync(
            request.Token, request.Email, cancellationToken);

        return result.Outcome switch
        {
            VerifyEmailOutcome.Success  => Ok(new { message = result.Message }),
            VerifyEmailOutcome.Expired  => StatusCode(410, new { message = result.Message }),
            VerifyEmailOutcome.Invalid  => BadRequest(new { message = result.Message }),
            _                          => BadRequest(new { message = result.Message }),
        };
    }

    // ────────────────────────────────────────────────────────────────────────────
    // POST /api/auth/resend-verification
    // ────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Re-sends a verification email. Rate-limited to 3 requests per 5 minutes per
    /// email address. Returns 429 with Retry-After header when exceeded.
    /// Always returns a generic success message to prevent user enumeration (OWASP A07).
    /// </summary>
    [HttpPost("resend-verification")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendVerification(
        [FromBody] ResendVerificationRequest request, CancellationToken cancellationToken)
    {
        var ipAddress = GetClientIpAddress();
        var result    = await _registrationService.ResendVerificationAsync(
            request.Email, ipAddress, cancellationToken);

        if (result.Outcome == ResendOutcome.RateLimited)
        {
            Response.Headers["Retry-After"] = "300"; // 5 minutes in seconds
            return StatusCode(429, new { message = result.Message });
        }

        // AC: return generic 200 for already-verified and not-found accounts.
        return Ok(new { message = result.Message });
    }

    // ────────────────────────────────────────────────────────────────────────────
    // POST /api/auth/check-email
    // ────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Checks whether an email address is available for registration.
    /// Returns <c>{ "available": true/false }</c>. Never reveals verification status (AC-4).
    /// Rate limited to prevent enumeration attacks (OWASP A07).
    /// </summary>
    [HttpPost("check-email")]
    [AllowAnonymous]
    [EnableRateLimiting("check-email-limit")]
    public async Task<IActionResult> CheckEmail(
        [FromBody] CheckEmailRequest request, CancellationToken cancellationToken)
    {
        var available = await _registrationService.IsEmailAvailableAsync(
            request.Email, cancellationToken);
        return Ok(new { available });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Issues full JWT + refresh token, updates LastLoginAt/LastLoginIp, logs Login audit,
    /// and registers the Redis session.
    /// Shared by the non-MFA login path and the MFA verify endpoint.
    /// </summary>
    private async Task<IActionResult> IssueFullTokensAsync(
        ApplicationUser user, string ipAddress, string userAgent, CancellationToken cancellationToken)
    {
        var concurrentCheck = await _sessionGuard.CheckAsync(
            user.Id.ToString(), ipAddress, cancellationToken);
        if (concurrentCheck == ConcurrentSessionResult.Blocked)
            return Conflict(new { message = "Another active session exists. Please logout first." });

        // Capture previous login info BEFORE overwriting (AC-4 — return previous to client).
        LastLoginInfo? lastLogin = user.LastLoginAt.HasValue
            ? new LastLoginInfo(
                user.LastLoginAt.Value.UtcDateTime.ToString("O"),
                user.LastLoginIp ?? string.Empty)
            : null;

        user.LastLoginAt = DateTimeOffset.UtcNow;
        user.LastLoginIp = ipAddress;
        await _userManager.UpdateAsync(user);

        var roles        = await _userManager.GetRolesAsync(user);
        var accessToken  = _tokenService.GenerateAccessToken(user, roles);
        var refreshToken = _tokenService.GenerateRefreshToken();
        AppendRefreshTokenCookie(refreshToken);

        var sessionId = Guid.NewGuid().ToString();
        await _sessionGuard.CreateAsync(user.Id.ToString(), sessionId, ipAddress, userAgent, cancellationToken);
        await _auditLogService.LogAsync(AuditAction.Login, user.Id, "User",
            ipAddress, userAgent, cancellationToken: cancellationToken);

        _logger.LogInformation("User {UserId} authenticated. SessionId={SessionId}.", user.Id, sessionId);
        return Ok(new AuthSuccessResponse(accessToken, LastLogin: lastLogin));
    }

    /// <summary>
    /// Returns the client IP, preferring X-Forwarded-For (behind reverse proxy).
    /// Uses only the leftmost IP to prevent header injection by intermediate proxies.
    /// </summary>
    private string GetClientIpAddress()
    {
        var forwarded = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',')[0].Trim();
            if (!string.IsNullOrWhiteSpace(first))
                return first;
        }
        return HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
    }

    private void AppendRefreshTokenCookie(string refreshToken)
    {
        Response.Cookies.Append(RefreshTokenCookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure   = true,
            SameSite = SameSiteMode.Strict,
            Expires  = DateTimeOffset.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays),
        });
    }
}

// ────────────────────────────────────────────────────────────────────────────────
// Request / Response DTOs
// ────────────────────────────────────────────────────────────────────────────────

/// <summary>Login request body.</summary>
public sealed record LoginRequest(string Email, string Password);

/// <summary>
/// Refresh request body. The expired access token is needed to extract user identity
/// server-side without a separate database lookup of the refresh token.
/// </summary>
public sealed record RefreshRequest(string AccessToken);

/// <summary>Successful token response — access token only; refresh token is in the cookie.</summary>
public sealed record TokenResponse(string AccessToken, string TokenType = "Bearer");

/// <summary>Email verification request body.</summary>
public sealed record VerifyEmailRequest(string Token, string Email);

/// <summary>Resend verification request body.</summary>
public sealed record ResendVerificationRequest(string Email);

/// <summary>Email availability check request body.</summary>
public sealed record CheckEmailRequest(string Email);
