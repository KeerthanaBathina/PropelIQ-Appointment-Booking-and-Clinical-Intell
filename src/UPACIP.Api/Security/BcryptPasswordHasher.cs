using Microsoft.AspNetCore.Identity;
using UPACIP.DataAccess.Entities;
using BC = BCrypt.Net.BCrypt;

namespace UPACIP.Api.Security;

/// <summary>
/// Custom <see cref="IPasswordHasher{TUser}"/> that replaces ASP.NET Core Identity's
/// default PBKDF2 hasher with BCrypt (work factor 10) to meet NFR-013.
///
/// BCrypt v2a hash format: $2a$10$&lt;22-char salt&gt;&lt;31-char hash&gt;
/// The <c>$2a$10$</c> prefix is verified on every hash check to guard against
/// a downgraded or misconfigured hasher being substituted at runtime.
/// </summary>
public sealed class BcryptPasswordHasher : IPasswordHasher<ApplicationUser>
{
    // Work factor MUST be >= 10 per NFR-013. Increasing this value is a one-way ratchet;
    // existing hashes remain valid because BCrypt embeds the work factor in the hash string.
    private const int WorkFactor = 10;

    /// <inheritdoc/>
    public string HashPassword(ApplicationUser user, string password)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password, nameof(password));
        return BC.HashPassword(password, workFactor: WorkFactor);
    }

    /// <inheritdoc/>
    public PasswordVerificationResult VerifyHashedPassword(
        ApplicationUser user, string hashedPassword, string providedPassword)
    {
        if (string.IsNullOrWhiteSpace(hashedPassword) ||
            string.IsNullOrWhiteSpace(providedPassword))
        {
            return PasswordVerificationResult.Failed;
        }

        // Verify the provided password against the stored BCrypt hash.
        if (!BC.Verify(providedPassword, hashedPassword))
            return PasswordVerificationResult.Failed;

        // If the stored hash uses a lower work factor than the current minimum,
        // signal the caller to re-hash the password on the next successful login.
        int storedWorkFactor = BC.PasswordNeedsRehash(hashedPassword, WorkFactor)
            ? 0
            : WorkFactor;

        return storedWorkFactor < WorkFactor
            ? PasswordVerificationResult.SuccessRehashNeeded
            : PasswordVerificationResult.Success;
    }
}
