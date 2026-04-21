namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Stores email verification tokens for the patient registration flow (US_012).
///
/// Security design:
///   - <see cref="TokenHash"/> stores the SHA-256 hash of the raw token, never the plaintext.
///     This means a database breach cannot be used to verify arbitrary accounts.
///   - <see cref="ExpiresAt"/> enforces the 1-hour expiry window (AC-3).
///   - <see cref="IsUsed"/> prevents replay attacks; a token may only confirm an account once.
///   - CASCADE DELETE on UserId ensures tokens are removed when the user account is hard-deleted.
///
/// NOTE: ASP.NET Core Identity generates the raw token via
/// <c>UserManager.GenerateEmailConfirmationTokenAsync</c>. This entity records
/// the hashed token so the verification event and its expiry can be queried independently
/// without storing the raw secret in the database.
/// </summary>
public sealed class EmailVerificationToken
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to <see cref="ApplicationUser"/> who owns this token.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// SHA-256 hash of the raw verification token (hex-encoded, lowercase).
    /// Never store the plaintext token in the database.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>UTC timestamp after which the token is considered expired (1 hour, AC-3).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Whether the token has already been used to confirm the account.
    /// Prevents replay attacks — once true, the token is invalid even if not yet expired.
    /// </summary>
    public bool IsUsed { get; set; } = false;

    /// <summary>UTC timestamp when this token record was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    /// <summary>The user account this token belongs to.</summary>
    public ApplicationUser User { get; set; } = null!;
}
