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
}
