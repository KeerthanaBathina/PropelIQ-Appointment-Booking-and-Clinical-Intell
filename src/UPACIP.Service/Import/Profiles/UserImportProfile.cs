using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Import.Models;

namespace UPACIP.Service.Import.Profiles;

/// <summary>
/// CSV import profile for <see cref="ApplicationUser"/> (staff/admin) entities
/// (US_092 task_001, AC-1, AC-4).
///
/// Required columns: email, role.
/// Optional columns: full_name.
///
/// Patient role is intentionally excluded — patients self-register.
/// Allowed roles: Staff, Admin.
///
/// Duplicate detection: unique email constraint (Identity framework).
/// PII columns: email — raw values are redacted in error reports.
///
/// Password: a random temporary password is hashed via BCrypt. Admin must force a
/// password reset via the standard password-reset workflow before first login.
///
/// Security (OWASP A07): role values are validated against an explicit allowlist.
/// </summary>
public sealed class UserImportProfile : ICsvImportProfile<ApplicationUser>
{
    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    private static readonly HashSet<string> AllowedRoles =
        new(StringComparer.OrdinalIgnoreCase) { "staff", "admin" };

    private readonly IPasswordHasher<ApplicationUser> _passwordHasher;

    public UserImportProfile(IPasswordHasher<ApplicationUser> passwordHasher)
    {
        _passwordHasher = passwordHasher;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ICsvImportProfile<ApplicationUser>
    // ─────────────────────────────────────────────────────────────────────────

    public string EntityTypeName => "User";

    public string[] RequiredColumns => ["email", "role"];

    public string[] OptionalColumns => ["full_name"];

    public HashSet<string> PiiColumns => ["email"];

    public ApplicationUser MapRow(Dictionary<string, string> row)
    {
        var email    = row.GetValueOrDefault("email",     string.Empty).Trim().ToLowerInvariant();
        var fullName = row.GetValueOrDefault("full_name", string.Empty).Trim();

        var parts     = fullName.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        var firstName = parts.Length > 0 ? parts[0] : string.Empty;
        var lastName  = parts.Length > 1 ? parts[1] : string.Empty;

        var user = new ApplicationUser
        {
            Id              = Guid.NewGuid(),
            UserName        = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email           = email,
            NormalizedEmail = email.ToUpperInvariant(),
            FirstName       = firstName,
            LastName        = lastName,
            FullName        = fullName,
            AccountStatus        = AccountStatus.PendingVerification,
            CreatedAt            = DateTimeOffset.UtcNow,
            UpdatedAt            = DateTimeOffset.UtcNow,
        };

        // Temporary password — admin must force a password reset on first login.
        var tempPassword = Guid.NewGuid().ToString("N");
        user.PasswordHash = _passwordHasher.HashPassword(user, tempPassword);

        return user;
    }

    public List<RowError> ValidateRow(Dictionary<string, string> row, int rowNumber)
    {
        var errors = new List<RowError>();

        // email
        var email = row.GetValueOrDefault("email", string.Empty).Trim();
        if (string.IsNullOrEmpty(email))
            errors.Add(new RowError { RowNumber = rowNumber, ColumnName = "email",
                ErrorMessage = "Email is required.", RawValue = "[REDACTED]" });
        else if (email.Length > 254 || !EmailRegex.IsMatch(email))
            errors.Add(new RowError { RowNumber = rowNumber, ColumnName = "email",
                ErrorMessage = "Invalid email format.", RawValue = "[REDACTED]" });

        // role
        var role = row.GetValueOrDefault("role", string.Empty).Trim();
        if (string.IsNullOrEmpty(role))
            errors.Add(new RowError { RowNumber = rowNumber, ColumnName = "role",
                ErrorMessage = "Role is required.", RawValue = string.Empty });
        else if (!AllowedRoles.Contains(role))
            errors.Add(new RowError { RowNumber = rowNumber, ColumnName = "role",
                ErrorMessage = "Invalid role. Allowed values: staff, admin.",
                RawValue = role.Length > 100 ? role[..100] : role });

        return errors;
    }

    public async Task<bool> IsDuplicateAsync(
        ApplicationUser      entity,
        ApplicationDbContext context,
        CancellationToken    ct = default)
    {
        return await context.Users
            .AnyAsync(u => u.NormalizedEmail == entity.NormalizedEmail, ct)
            .ConfigureAwait(false);
    }

    public string DuplicateKey(ApplicationUser entity) => "user:email=[REDACTED]";

    // ─────────────────────────────────────────────────────────────────────────
    // Role extraction (used by engine to assign the Identity role after insert)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the normalised role name from the CSV row (e.g. "Staff" or "Admin").
    /// </summary>
    public static string ExtractRole(Dictionary<string, string> row)
    {
        var role = row.GetValueOrDefault("role", string.Empty).Trim().ToLowerInvariant();
        return role switch
        {
            "admin" => "Admin",
            _       => "Staff",
        };
    }
}
