using System.ComponentModel.DataAnnotations;

namespace UPACIP.Api.Models;

/// <summary>Response returned from POST /api/auth/mfa/setup.</summary>
public sealed record MfaSetupResponse(
    /// <summary>Full OTP auth URL for QR code rendering.</summary>
    string OtpAuthUrl,
    /// <summary>Base32-encoded secret for manual entry into authenticator apps.</summary>
    string ManualEntryKey);

/// <summary>Request body for POST /api/auth/mfa/verify (login MFA step).</summary>
public sealed record MfaVerifyRequest(
    [Required] string MfaToken,
    [Required][StringLength(8, MinimumLength = 6)] string Code);

/// <summary>Request body for POST /api/auth/mfa/verify-setup (enable MFA).</summary>
public sealed record MfaVerifySetupRequest(
    [Required][StringLength(6, MinimumLength = 6)] string Code);

/// <summary>Request body for POST /api/auth/mfa/disable.</summary>
public sealed record MfaDisableRequest(
    [Required] string Password);

/// <summary>Last-login metadata returned in the login success response (US_016 AC-4).</summary>
public sealed record LastLoginInfo(
    /// <summary>UTC ISO-8601 timestamp of the previous login.</summary>
    string Timestamp,
    /// <summary>IP address used for the previous login.</summary>
    string IpAddress);

/// <summary>
/// Extended token response that includes last-login metadata (US_016 AC-4).
/// Replaces the bare <see cref="TokenResponse"/> for successful credential + MFA-free login
/// and for the MFA verify endpoint.
/// </summary>
public sealed record AuthSuccessResponse(
    string AccessToken,
    string TokenType = "Bearer",
    LastLoginInfo? LastLogin = null);
