using Microsoft.AspNetCore.Identity;
using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Platform user extending ASP.NET Core Identity with UPACIP-specific profile fields.
/// Guid PK ensures IDs are non-enumerable (security — prevents ID-enumeration attacks).
/// </summary>
public sealed class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>Patient/staff first name (max 100 chars).</summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>Patient/staff last name (max 100 chars).</summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>Computed full display name (FirstName + LastName).</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Optional date of birth used for patient eligibility checks.</summary>
    public DateOnly? DateOfBirth { get; set; }

    /// <summary>
    /// Account lifecycle state (US_012, AC-1, AC-2).
    /// Defaults to <see cref="AccountStatus.PendingVerification"/> on creation;
    /// transitions to <see cref="AccountStatus.Active"/> after email confirmation.
    /// </summary>
    public AccountStatus AccountStatus { get; set; } = AccountStatus.PendingVerification;

    /// <summary>UTC timestamp when the user record was first created.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>UTC timestamp of the last profile update.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Soft-delete sentinel (DR-021). Null = active; non-null = logically deleted.
    /// Hard deletes are forbidden to preserve audit trails.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    /// <summary>Documents uploaded by this user.</summary>
    public ICollection<ClinicalDocument> UploadedDocuments { get; set; } = [];

    /// <summary>Audit trail entries attributed to this user.</summary>
    public ICollection<AuditLog> AuditLogs { get; set; } = [];

    /// <summary>Email verification tokens issued for this user.</summary>
    public ICollection<EmailVerificationToken> EmailVerificationTokens { get; set; } = [];

    /// <summary>Password reset tokens issued for this user (US_015).</summary>
    public ICollection<PasswordResetToken> PasswordResetTokens { get; set; } = [];

    // -------------------------------------------------------------------------
    // MFA fields (US_016)
    // -------------------------------------------------------------------------

    /// <summary>
    /// AES-256 encrypted TOTP secret (Base64-encoded IV + ciphertext).
    /// Never stored in plaintext. Null until MFA is enabled.
    /// </summary>
    public string? TotpSecretEncrypted { get; set; }

    /// <summary>
    /// JSON array of BCrypt-hashed backup codes (8 codes, each one-time use).
    /// Null until MFA is enabled.
    /// </summary>
    public string? MfaRecoveryCodes { get; set; }

    /// <summary>Whether TOTP-based MFA is active for this user.</summary>
    public bool MfaEnabled { get; set; }

    // -------------------------------------------------------------------------
    // Login tracking fields (US_016 AC-4)
    // -------------------------------------------------------------------------

    /// <summary>UTC timestamp of the user's most recent successful authentication.</summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Client IP address at the time of the last successful login (IPv4 or IPv6).</summary>
    public string? LastLoginIp { get; set; }
}
