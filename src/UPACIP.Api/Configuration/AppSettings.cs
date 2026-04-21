using System.ComponentModel.DataAnnotations;

namespace UPACIP.Api.Configuration;

/// <summary>
/// Strongly-typed root settings POCO that mirrors the structure of
/// <c>appsettings.json</c>.  Registered with
/// <c>AddOptionsWithValidateOnStart&lt;AppSettings&gt;()</c> so that missing or
/// invalid required fields cause the application to fail fast at startup rather
/// than silently producing runtime errors.
///
/// Hot-reload note: inject <c>IOptionsMonitor&lt;AppSettings&gt;</c> in services
/// that must react to in-process changes (e.g. log-level, feature flags).
/// Connection strings and Kestrel bindings require a process restart.
/// </summary>
public sealed class AppSettings
{
    // -------------------------------------------------------------------------
    // CORS
    // -------------------------------------------------------------------------

    public CorsSettingsSection CorsSettings { get; init; } = new();

    // -------------------------------------------------------------------------
    // JWT (structural validation only — signing key is in user secrets)
    // -------------------------------------------------------------------------

    [Required]
    public JwtSettingsSection JwtSettings { get; init; } = new();

    // -------------------------------------------------------------------------
    // MFA (encryption key must be supplied via user secrets)
    // -------------------------------------------------------------------------

    public MfaSettingsSection MfaSettings { get; init; } = new();

    // -------------------------------------------------------------------------
    // Nested section POCOs
    // -------------------------------------------------------------------------

    public sealed class CorsSettingsSection
    {
        /// <summary>
        /// One or more allowed origins for the CORS policy.  Must not be empty in
        /// production.  Defaults to an empty array so development without explicit
        /// config does not throw.
        /// </summary>
        public string[] AllowedOrigins { get; init; } = [];
    }

    public sealed class JwtSettingsSection
    {
        [Required(AllowEmptyStrings = false, ErrorMessage = "JwtSettings:Issuer is required.")]
        public string Issuer { get; init; } = string.Empty;

        [Required(AllowEmptyStrings = false, ErrorMessage = "JwtSettings:Audience is required.")]
        public string Audience { get; init; } = string.Empty;

        [Range(1, 60, ErrorMessage = "JwtSettings:AccessTokenExpiryMinutes must be between 1 and 60.")]
        public int AccessTokenExpiryMinutes { get; init; } = 15;

        [Range(1, 30, ErrorMessage = "JwtSettings:RefreshTokenExpiryDays must be between 1 and 30.")]
        public int RefreshTokenExpiryDays { get; init; } = 7;
    }

    public sealed class MfaSettingsSection
    {
        /// <summary>
        /// AES-256 encryption key material for TOTP secrets. Must be set via user secrets.
        /// <c>dotnet user-secrets set "Mfa:TotpEncryptionKey" "&lt;32+ char random value&gt;"</c>
        /// </summary>
        public string TotpEncryptionKey { get; init; } = string.Empty;
    }
}
