namespace UPACIP.Service.Auth;

/// <summary>
/// Service contract for TOTP-based Multi-Factor Authentication operations (US_016 AC-1).
///
/// Security guarantees:
///   - TOTP secrets are AES-256 encrypted before persistence; never stored in plaintext.
///   - Backup codes are BCrypt-hashed; each code is single-use (marked consumed on first use).
///   - Code verification uses RFC 6238 with ±1 time-step tolerance (30-second window).
/// </summary>
public interface IMfaService
{
    /// <summary>
    /// Generates a new TOTP secret for the given user, encrypts it, persists it (MFA not yet
    /// enabled), and returns the OTP auth URL for QR code display and the Base32 manual-entry key.
    /// Calling this again before <see cref="EnableMfaAsync"/> replaces the pending secret.
    /// </summary>
    Task<MfaSetupData> GenerateSecretAsync(Guid userId, string userEmail, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the user-supplied TOTP <paramref name="code"/> against the stored encrypted secret.
    /// Returns <c>true</c> when the code is valid within ±1 step (RFC 6238).
    /// Returns <c>false</c> when the user has no stored secret or MFA is not enabled.
    /// </summary>
    Task<bool> VerifyTotpCodeAsync(Guid userId, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies the supplied <paramref name="code"/> against the pending (pre-enable) secret.
    /// On success, sets <c>MfaEnabled = true</c> and generates 8 single-use BCrypt-hashed backup codes.
    /// Returns the plaintext backup codes (shown once) or <c>null</c> on failure.
    /// </summary>
    Task<string[]?> EnableMfaAsync(Guid userId, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the supplied <paramref name="code"/> matches any unused BCrypt-hashed backup code.
    /// On success, marks the code as consumed (one-time use) and returns <c>true</c>.
    /// </summary>
    Task<bool> VerifyBackupCodeAsync(Guid userId, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears the encrypted TOTP secret, backup codes, and sets <c>MfaEnabled = false</c>.
    /// Used for user-initiated disable and admin MFA reset flows.
    /// </summary>
    Task DisableMfaAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Returns <c>true</c> when MFA is currently enabled for the specified user.</summary>
    Task<bool> IsEnabledAsync(Guid userId, CancellationToken cancellationToken = default);
}

/// <summary>Data returned after generating a new TOTP secret (pre-enable step).</summary>
public sealed record MfaSetupData(
    /// <summary>Full OTP auth URL for QR code rendering (<c>otpauth://totp/...</c>).</summary>
    string OtpAuthUrl,
    /// <summary>Base32-encoded secret key for manual entry into authenticator apps.</summary>
    string ManualEntryKey);
