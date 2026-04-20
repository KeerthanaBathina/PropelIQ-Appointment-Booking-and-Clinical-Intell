using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using UPACIP.DataAccess.Entities;
using UPACIP.Service.Auth;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Authentication endpoints — login, refresh, and logout.
/// Refresh tokens are transmitted exclusively via HttpOnly Secure SameSite=Strict cookies
/// so they are inaccessible to JavaScript (OWASP: HttpOnly cookie protection).
/// </summary>
[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private const string RefreshTokenCookieName = "refreshToken";

    private readonly UserManager<ApplicationUser>  _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ITokenService _tokenService;
    private readonly JwtSettings   _jwtSettings;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<ApplicationUser>  userManager,
        SignInManager<ApplicationUser> signInManager,
        ITokenService tokenService,
        JwtSettings   jwtSettings,
        ILogger<AuthController> logger)
    {
        _userManager   = userManager;
        _signInManager = signInManager;
        _tokenService  = tokenService;
        _jwtSettings   = jwtSettings;
        _logger        = logger;
    }

    // ────────────────────────────────────────────────────────────────────────────
    // POST /api/auth/login
    // ────────────────────────────────────────────────────────────────────────────
    /// <summary>
    /// Validates credentials and issues a JWT access token (response body) and refresh
    /// token (HttpOnly cookie). Lockout is enforced per NFR-016 (5 attempts / 30 min).
    /// </summary>
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        // Lookup — use email as the unique identifier (UniqueEmail enforced in Identity options)
        var user = await _userManager.FindByEmailAsync(request.Email);

        // Return the same generic error for unknown email as for wrong password
        // to prevent user enumeration (OWASP A07).
        if (user is null)
            return Unauthorized(new { message = "Invalid credentials." });

        if (user.DeletedAt is not null)
            return Unauthorized(new { message = "Invalid credentials." });

        var result = await _signInManager.CheckPasswordSignInAsync(
            user, request.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            _logger.LogWarning("Login locked out for user {UserId}.", user.Id);
            return StatusCode(429, new { message = "Account locked. Please try again in 30 minutes." });
        }

        if (!result.Succeeded)
            return Unauthorized(new { message = "Invalid credentials." });

        var roles        = await _userManager.GetRolesAsync(user);
        var accessToken  = _tokenService.GenerateAccessToken(user, roles);
        var refreshToken = _tokenService.GenerateRefreshToken();

        AppendRefreshTokenCookie(refreshToken);

        _logger.LogInformation("User {UserId} authenticated successfully.", user.Id);
        return Ok(new TokenResponse(accessToken));
    }

    // ────────────────────────────────────────────────────────────────────────────
    // POST /api/auth/refresh
    // ────────────────────────────────────────────────────────────────────────────
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
            return Unauthorized(new { message = "Session expired. Please log in again." });

        if (await _tokenService.IsRefreshTokenBlacklistedAsync(refreshToken, cancellationToken))
            return Unauthorized(new { message = "Session expired. Please log in again." });

        var principal = _tokenService.GetPrincipalFromExpiredToken(request.AccessToken);
        if (principal is null)
            return Unauthorized(new { message = "Session expired. Please log in again." });

        // The sub claim contains the user's GUID (set in TokenService.GenerateAccessToken).
        var userIdValue = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdValue, out var userId))
            return Unauthorized(new { message = "Session expired. Please log in again." });

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || user.DeletedAt is not null)
            return Unauthorized(new { message = "Session expired. Please log in again." });

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
    [Authorize]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        var refreshToken = Request.Cookies[RefreshTokenCookieName];

        if (!string.IsNullOrWhiteSpace(refreshToken))
            await _tokenService.BlacklistRefreshTokenAsync(refreshToken, cancellationToken);

        Response.Cookies.Delete(RefreshTokenCookieName);

        return NoContent();
    }

    // ────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────────
    private void AppendRefreshTokenCookie(string refreshToken)
    {
        Response.Cookies.Append(RefreshTokenCookieName, refreshToken, new CookieOptions
        {
            HttpOnly = true,                        // Not accessible to JavaScript (OWASP HttpOnly)
            Secure   = true,                        // HTTPS-only transmission
            SameSite = SameSiteMode.Strict,         // No cross-origin cookie sending
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
