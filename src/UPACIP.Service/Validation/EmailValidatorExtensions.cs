using System.Text.RegularExpressions;
using FluentValidation;

namespace UPACIP.Service.Validation;

/// <summary>
/// Reusable FluentValidation rule extensions for email address validation (DR-011).
///
/// Usage — apply to any validator that has a string email property:
/// <code>
///     RuleFor(x => x.Email).ApplyEmailRule();
/// </code>
/// </summary>
public static partial class EmailValidatorExtensions
{
    /// <summary>
    /// RFC 5321-compatible email pattern per DR-011.
    /// Allows standard local-part characters, a single '@', a domain label
    /// chain, and a TLD of at least 2 characters.
    /// </summary>
    private const string EmailPattern =
        @"^[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}$";

    // Compiled once at class load for performance (NFR-030).
    private static readonly Regex EmailRegex = BuildEmailRegex();

    /// <summary>
    /// Applies the UPACIP email format validation rule to a string property.
    /// Returns a 400-compatible error message when the format is invalid.
    /// </summary>
    public static IRuleBuilderOptions<T, string> ApplyEmailRule<T>(
        this IRuleBuilder<T, string> ruleBuilder)
    {
        return ruleBuilder
            .NotEmpty()
                .WithMessage("Email address is required.")
            .MaximumLength(256)
                .WithMessage("Email address must not exceed 256 characters.")
            .Must(email => EmailRegex.IsMatch(email))
                .WithMessage("Email must be in a valid format (e.g., user@example.com).");
    }

    /// <summary>
    /// Applies the email validation rule to a nullable string property.
    /// Empty/null values pass (use <c>.NotEmpty()</c> separately when required).
    /// </summary>
    public static IRuleBuilderOptions<T, string?> ApplyOptionalEmailRule<T>(
        this IRuleBuilder<T, string?> ruleBuilder)
    {
        return ruleBuilder
            .MaximumLength(256)
                .WithMessage("Email address must not exceed 256 characters.")
            .Must(email => email is null || EmailRegex.IsMatch(email))
                .WithMessage("Email must be in a valid format (e.g., user@example.com).");
    }

    [GeneratedRegex(EmailPattern, RegexOptions.Compiled | RegexOptions.CultureInvariant)]
    private static partial Regex BuildEmailRegex();
}
