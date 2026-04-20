namespace UPACIP.Service.Auth;

/// <summary>
/// Configuration POCO for JWT token issuance and validation.
/// Bound from the "JwtSettings" section in appsettings.json.
/// SECURITY: <see cref="SigningKey"/> MUST be loaded from user secrets or environment
/// variables — never committed to source control. Minimum 32 characters (256-bit) for
/// HMAC-SHA256 as required by RFC 7518.
/// </summary>
public sealed class JwtSettings
{
    /// <summary>Identifies the principal that issued the JWT (iss claim).</summary>
    public string Issuer { get; init; } = string.Empty;

    /// <summary>Identifies the recipients the JWT is intended for (aud claim).</summary>
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// HMAC-SHA256 signing key — minimum 32 characters.
    /// Load from user secrets: dotnet user-secrets set "JwtSettings:SigningKey" "&lt;value&gt;"
    /// </summary>
    public string SigningKey { get; init; } = string.Empty;

    /// <summary>Access token lifetime in minutes. Default: 15 (AC-1).</summary>
    public int AccessTokenExpiryMinutes { get; init; } = 15;

    /// <summary>Refresh token lifetime in days. Default: 7 (AC-1).</summary>
    public int RefreshTokenExpiryDays { get; init; } = 7;
}
