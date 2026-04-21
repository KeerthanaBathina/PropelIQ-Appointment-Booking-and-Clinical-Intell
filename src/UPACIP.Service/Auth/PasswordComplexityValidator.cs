using Microsoft.AspNetCore.Identity;
using UPACIP.DataAccess.Entities;

namespace UPACIP.Service.Auth;

/// <summary>
/// Custom ASP.NET Core Identity password validator enforcing UPACIP complexity rules
/// (NFR-013 / AC-5): 8+ characters, at least 1 uppercase, 1 digit, 1 special character.
///
/// Registered via <c>IdentityBuilder.AddPasswordValidator</c> in Program.cs and runs
/// alongside (not instead of) Identity's built-in options.Password rules.
///
/// Returns individual <see cref="IdentityError"/> per failing rule so callers can surface
/// specific missing criteria to the user.
/// </summary>
public sealed class PasswordComplexityValidator : IPasswordValidator<ApplicationUser>
{
    public Task<IdentityResult> ValidateAsync(
        UserManager<ApplicationUser> manager,
        ApplicationUser user,
        string? password)
    {
        var errors = new List<IdentityError>();

        if (string.IsNullOrEmpty(password))
        {
            errors.Add(new IdentityError
            {
                Code = "PasswordRequired",
                Description = "Password is required.",
            });
            return Task.FromResult(IdentityResult.Failed(errors.ToArray()));
        }

        if (password.Length < 8)
            errors.Add(new IdentityError
            {
                Code = "PasswordTooShort",
                Description = "Password must be at least 8 characters.",
            });

        if (!password.Any(char.IsUpper))
            errors.Add(new IdentityError
            {
                Code = "PasswordRequiresUpper",
                Description = "Password must contain at least 1 uppercase letter.",
            });

        if (!password.Any(char.IsDigit))
            errors.Add(new IdentityError
            {
                Code = "PasswordRequiresDigit",
                Description = "Password must contain at least 1 number.",
            });

        if (password.All(c => char.IsLetterOrDigit(c)))
            errors.Add(new IdentityError
            {
                Code = "PasswordRequiresNonAlphanumeric",
                Description = "Password must contain at least 1 special character.",
            });

        return Task.FromResult(
            errors.Count == 0 ? IdentityResult.Success : IdentityResult.Failed(errors.ToArray()));
    }
}
