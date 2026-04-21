namespace UPACIP.DataAccess.Enums;

public enum AuditAction
{
    Login,
    Logout,
    DataAccess,
    DataModify,
    DataDelete,
    /// <summary>Authenticated user attempted a route outside their role (HTTP 403).</summary>
    AccessDenied,
    /// <summary>Unauthenticated or token-rejected request (HTTP 401).</summary>
    AuthFailure,
    /// <summary>Password reset was requested for a user account (FR-006).</summary>
    PasswordResetRequest,
    /// <summary>Password was successfully reset via the reset link (FR-006).</summary>
    PasswordResetSuccess,
    /// <summary>Password reset attempt failed (invalid or expired token) (FR-006).</summary>
    PasswordResetFailure,
    /// <summary>Credential validation failed (wrong password) — includes remaining attempts (US_016 AC-2).</summary>
    FailedLogin,
    /// <summary>Account locked after exceeding the maximum failed-attempt threshold (US_016 AC-2).</summary>
    AccountLocked,
    /// <summary>TOTP-based MFA was enabled for the user (US_016 AC-1).</summary>
    MfaEnabled,
    /// <summary>TOTP-based MFA was disabled by the user (US_016).</summary>
    MfaDisabled,
    /// <summary>MFA TOTP code was successfully verified during login (US_016 AC-1).</summary>
    MfaVerified,
    /// <summary>An admin reset MFA for another user (US_016 edge case).</summary>
    AdminMfaReset,
}
