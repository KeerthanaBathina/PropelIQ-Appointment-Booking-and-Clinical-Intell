using System.Text.RegularExpressions;
using FluentValidation;

namespace UPACIP.Service.Validation;

/// <summary>
/// Reusable FluentValidation rule extensions for email address validation (DR-011, US_085 AC-2).
///
/// Changes in US_085:
///   - Maximum length reduced to 254 characters (RFC 5321 section 4.5.3.1.3).
///   - Error message updated with format hint for clearer user guidance (AC-2).
///   - Regex evaluation capped at 100 ms to prevent ReDoS attacks from maliciously
///     crafted input strings (OWASP A03).
///   - Overload added to accept a caller-supplied <see cref="Regex"/> for hot-reload
///     support via <c>IOptionsMonitor&lt;ValidationRuleOptions&gt;</c> (edge case 1).
///
/// Usage — apply to any validator that has a string email property:
/// <code>
///     // Static compiled regex:
///     RuleFor(x => x.Email).ApplyEmailRule();
///
///     // Configurable regex from IOptionsMonitor:
///     RuleFor(x => x.Email).ApplyEmailRule(myRegex);
/// </code>
/// </summary>
public static class EmailValidatorExtensions
{
    /// <summary>
    /// RFC 5321-compatible email pattern per DR-011.
    /// Allows standard local-part characters, a single '@', a domain label
    /// chain, and a TLD of at least 2 characters.
    /// </summary>
    private const string DefaultEmailPattern =
        @"^[a-zA-Z0-9._%+\-]+@[a-zA-Z0-9.\-]+\.[a-zA-Z]{2,}$";

    /// <summary>
    /// Default compiled regex with a 100 ms match timeout to guard against
    /// ReDoS attacks from maliciously long input strings (OWASP A03).
    /// </summary>
    private static readonly Regex DefaultEmailRegex = new Regex(
        DefaultEmailPattern,
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    // ── Public extension methods ───────────────────────────────────────────────

    /// <summary>
    /// Applies the UPACIP email format validation rule using the default compiled regex.
    /// Returns a 400-compatible error message when the format is invalid.
    /// </summary>
    public static IRuleBuilderOptions<T, string> ApplyEmailRule<T>(
        this IRuleBuilder<T, string> ruleBuilder)
        => ApplyEmailRule(ruleBuilder, DefaultEmailRegex);

    /// <summary>
    /// Applies email format validation using <paramref name="customRegex"/>.
    /// Use this overload when the regex is loaded from <c>IOptionsMonitor&lt;ValidationRuleOptions&gt;</c>
    /// to support hot-reload without application restart (edge case 1, US_085).
    /// The caller must ensure <paramref name="customRegex"/> was compiled with a
    /// <c>MatchTimeout</c> to prevent ReDoS (OWASP A03).
    /// </summary>
    public static IRuleBuilderOptions<T, string> ApplyEmailRule<T>(
        this IRuleBuilder<T, string> ruleBuilder,
        Regex customRegex)
    {
        return ruleBuilder
            .NotEmpty()
                .WithMessage("Email address is required.")
            .MaximumLength(254)
                .WithMessage("Email address must not exceed 254 characters (RFC 5321).")
            .Must(email => SafeIsMatch(customRegex, email))
                .WithMessage(
                    "Invalid email format. Expected format: user@example.com. " +
                    "Received: '{PropertyValue}'.");
    }

    /// <summary>
    /// Applies the email validation rule to a nullable string property using the default regex.
    /// Empty/null values pass (use <c>.NotEmpty()</c> separately when required).
    /// </summary>
    public static IRuleBuilderOptions<T, string?> ApplyOptionalEmailRule<T>(
        this IRuleBuilder<T, string?> ruleBuilder)
    {
        return ruleBuilder
            .MaximumLength(254)
                .WithMessage("Email address must not exceed 254 characters (RFC 5321).")
            .Must(email => email is null || SafeIsMatch(DefaultEmailRegex, email))
                .WithMessage(
                    "Invalid email format. Expected format: user@example.com. " +
                    "Received: '{PropertyValue}'.");
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Evaluates <paramref name="regex"/> against <paramref name="input"/>, returning
    /// <c>false</c> on <see cref="RegexMatchTimeoutException"/> rather than propagating
    /// the exception (treats timeout as a non-match, denying maliciously crafted input).
    /// </summary>
    private static bool SafeIsMatch(Regex regex, string input)
    {
        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
