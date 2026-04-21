using System.Security.Claims;
using UPACIP.DataAccess.Entities;

namespace UPACIP.Service.Auth;

/// <summary>
/// Abstraction for JWT access token issuance, refresh token lifecycle, and Redis blacklisting.
/// All feature controllers that need token operations should consume this interface.
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Generates a signed JWT access token containing sub, email, role, and jti claims.
    /// Expiry is controlled by <see cref="JwtSettings.AccessTokenExpiryMinutes"/> (default 15 min).
    /// </summary>
    string GenerateAccessToken(ApplicationUser user, IList<string> roles);

    /// <summary>
    /// Generates a cryptographically random refresh token (64 bytes, Base64Url encoded).
    /// The caller is responsible for transmitting it via HttpOnly cookie only.
    /// </summary>
    string GenerateRefreshToken();

    /// <summary>
    /// Validates the JWT signature and claims but ignores token expiry.
    /// Used exclusively in the refresh flow to identify the user from an expired access token.
    /// Returns <c>null</c> if the token is tampered or uses an unexpected algorithm.
    /// </summary>
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);

    /// <summary>
    /// Adds the SHA-256 hash of <paramref name="refreshToken"/> to the Redis blacklist so it
    /// cannot be reused after logout or token rotation (AC-4).
    /// TTL matches <see cref="JwtSettings.RefreshTokenExpiryDays"/> so entries self-expire.
    /// </summary>
    Task BlacklistRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <c>true</c> when <paramref name="refreshToken"/> has been blacklisted.
    /// </summary>
    Task<bool> IsRefreshTokenBlacklistedAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the JWT ID (<paramref name="jti"/>) to the Redis access-token blacklist so
    /// the corresponding access token cannot be used again after logout or forced session
    /// invalidation (AC-1, task_002 step 5).
    /// TTL is set to <paramref name="remaining"/> so the entry self-expires once the token
    /// would have expired naturally — no permanent entries are created.
    /// </summary>
    Task BlacklistJtiAsync(string jti, TimeSpan remaining, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <c>true</c> when the JWT ID <paramref name="jti"/> has been blacklisted.
    /// JWT validation middleware checks this before allowing any authenticated request.
    /// </summary>
    Task<bool> IsJtiBlacklistedAsync(string jti, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a short-lived (5-minute) MFA verification token for the given user.
    /// The token contains <c>sub</c> (userId), <c>jti</c>, and a <c>purpose=mfa-verification</c>
    /// claim. It carries NO role claims and must not be accepted as a full access token.
    /// </summary>
    string GenerateMfaToken(ApplicationUser user);

    /// <summary>
    /// Validates an MFA token: checks signature, expiry, and that the <c>purpose</c> claim
    /// equals <c>mfa-verification</c>. Returns the claims principal on success, or <c>null</c>
    /// when the token is invalid, expired, or tampered.
    /// </summary>
    ClaimsPrincipal? ValidateMfaToken(string mfaToken);
}
