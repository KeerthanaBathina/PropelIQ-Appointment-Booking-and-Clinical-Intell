using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using UPACIP.DataAccess.Entities;
using UPACIP.Service.Caching;

namespace UPACIP.Service.Auth;

/// <summary>
/// JWT token service implementing <see cref="ITokenService"/>.
///
/// Security design:
///   - Access tokens signed with HMAC-SHA256 using a 256-bit key from configuration (never hardcoded).
///   - Refresh tokens are 64 cryptographically random bytes encoded as Base64Url.
///   - Blacklisted tokens are stored in Redis by SHA-256 hash of the raw token (raw token
///     never appears as a Redis key or value, preventing token sidejacking via Redis key inspection).
///   - Token rotation: every refresh call blacklists the consumed token before issuing a new one.
///   - Clock skew is zero — 15-minute access token window is exact.
/// </summary>
public sealed class TokenService : ITokenService
{
    private const string BlacklistKeyPrefix = "rt:blacklist:";

    private readonly JwtSettings _settings;
    private readonly ICacheService _cache;
    private readonly ILogger<TokenService> _logger;

    public TokenService(JwtSettings settings, ICacheService cache, ILogger<TokenService> logger)
    {
        _settings = settings;
        _cache    = cache;
        _logger   = logger;
    }

    /// <inheritdoc/>
    public string GenerateAccessToken(ApplicationUser user, IList<string> roles)
    {
        var keyBytes    = Encoding.UTF8.GetBytes(_settings.SigningKey);
        var securityKey = new SymmetricSecurityKey(keyBytes);
        var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

        // Claims included in every access token (AC-2).
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub,   user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email!),
            new(JwtRegisteredClaimNames.Jti,   Guid.NewGuid().ToString()),
        };

        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        var token = new JwtSecurityToken(
            issuer:             _settings.Issuer,
            audience:           _settings.Audience,
            claims:             claims,
            notBefore:          DateTime.UtcNow,
            expires:            DateTime.UtcNow.AddMinutes(_settings.AccessTokenExpiryMinutes),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <inheritdoc/>
    public string GenerateRefreshToken()
    {
        // 64 bytes = 512 bits of entropy; Base64Url encoding keeps it URL-safe for cookies.
        var bytes = RandomNumberGenerator.GetBytes(64);
        return Base64UrlEncoder.Encode(bytes);
    }

    /// <inheritdoc/>
    public ClaimsPrincipal? GetPrincipalFromExpiredToken(string token)
    {
        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidIssuer              = _settings.Issuer,
            ValidateAudience         = true,
            ValidAudience            = _settings.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(_settings.SigningKey)),
            // Intentionally skip lifetime check — the token is expected to be expired
            // in the refresh flow; signature validity is what matters here.
            ValidateLifetime = false,
            ClockSkew        = TimeSpan.Zero,
        };

        try
        {
            var handler   = new JwtSecurityTokenHandler();
            var principal = handler.ValidateToken(token, validationParams, out var securityToken);

            // Guard against algorithm substitution attacks — only HMAC-SHA256 is acceptable.
            if (securityToken is not JwtSecurityToken jwtToken ||
                !jwtToken.Header.Alg.Equals(
                    SecurityAlgorithms.HmacSha256, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Refresh rejected: token uses unexpected algorithm '{Alg}'.",
                    (securityToken as JwtSecurityToken)?.Header?.Alg ?? "unknown");
                return null;
            }

            return principal;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract principal from expired token.");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task BlacklistRefreshTokenAsync(
        string refreshToken, CancellationToken cancellationToken = default)
    {
        var key = BuildBlacklistKey(refreshToken);
        // TTL matches the maximum remaining validity of any refresh token so entries self-expire.
        await _cache.SetAsync(
            key,
            "1",
            TimeSpan.FromDays(_settings.RefreshTokenExpiryDays),
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> IsRefreshTokenBlacklistedAsync(
        string refreshToken, CancellationToken cancellationToken = default)
    {
        var key    = BuildBlacklistKey(refreshToken);
        var result = await _cache.GetAsync<string>(key, cancellationToken);
        return result is not null;
    }

    /// <summary>
    /// Returns the Redis cache key for a refresh token blacklist entry.
    /// Uses SHA-256 hash so the raw token value never appears in Redis keys.
    /// </summary>
    private static string BuildBlacklistKey(string refreshToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return $"{BlacklistKeyPrefix}{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
