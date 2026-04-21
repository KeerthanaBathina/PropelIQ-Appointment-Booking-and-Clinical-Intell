namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Stores password reset tokens for the password-reset flow (US_015, FR-005).
///
/// Security design (mirrors <see cref="EmailVerificationToken"/>):
///   - <see cref="TokenHash"/> stores the SHA-256 hash of the raw Identity token — never plaintext.
///   - <see cref="ExpiresAt"/> enforces the 1-hour expiry window (FR-005, AC-3).
///   - <see cref="IsUsed"/> prevents replay attacks; a token is single-use.
///   - <see cref="InvalidatedAt"/> allows bulk-invalidation of all prior tokens when a new
///     request is made (edge case: only the latest token is valid).
///   - CASCADE DELETE on UserId ensures tokens are removed when the account is deleted.
/// </summary>
public sealed class PasswordResetToken
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>FK to <see cref="ApplicationUser"/> who requested this token.</summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// SHA-256 hash of the raw ASP.NET Core Identity password reset token (hex-encoded, lowercase).
    /// Never store the plaintext token in the database.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>UTC timestamp after which the token is considered expired (1 hour, FR-005).</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Whether this token has already been consumed by a successful password reset.
    /// Prevents replay attacks.
    /// </summary>
    public bool IsUsed { get; set; } = false;

    /// <summary>
    /// UTC timestamp when this token was explicitly invalidated by a subsequent reset request.
    /// Null when the token is still valid (not superseded).
    /// Edge case: only the latest token per user is valid — earlier tokens are bulk-invalidated
    /// by setting this field when a new token is generated.
    /// </summary>
    public DateTime? InvalidatedAt { get; set; }

    /// <summary>UTC timestamp when this token record was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // -------------------------------------------------------------------------
    // Token lifecycle states
    // -------------------------------------------------------------------------
    //
    // Active:      is_used = false  AND  invalidated_at IS NULL  AND  expires_at > NOW()
    // Used:        is_used = true   — consumed by a successful password reset; cannot be reused
    // Expired:     expires_at <= NOW()  — 1-hour window elapsed; HTTP 410 returned to client
    // Invalidated: invalidated_at IS NOT NULL  — superseded by a newer token for the same user
    //
    // Validation query:
    //   WHERE token_hash = @hash
    //     AND is_used = false
    //     AND invalidated_at IS NULL
    //     AND expires_at > @now
    //
    // Cleanup strategy (out of scope — future infrastructure task):
    //   IHostedService job running daily:
    //     DELETE FROM password_reset_tokens
    //     WHERE expires_at < NOW() - INTERVAL '7 days'
    //   Retains 7 days of expired rows for audit trail before purging.

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    /// <summary>The user account this token belongs to.</summary>
    public ApplicationUser User { get; set; } = null!;
}
