using FluentValidation;
using UPACIP.Service.Auth;

namespace UPACIP.Service.Validation;

/// <summary>
/// FluentValidation rules for <see cref="RegistrationRequest"/>.
/// Registered via assembly scanning in Program.cs (same mechanism as AppointmentDateValidator).
///
/// Password complexity requirements per NFR-013 / AC-5:
///   - 8+ characters
///   - At least 1 uppercase letter
///   - At least 1 digit
///   - At least 1 special character
///
/// Each failing rule emits an individual message so the client can display
/// specific missing criteria without ambiguity.
/// </summary>
public sealed class RegistrationRequestValidator : AbstractValidator<RegistrationRequest>
{
    public RegistrationRequestValidator()
    {
        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required.")
            .MaximumLength(50).WithMessage("First name must be 50 characters or fewer.");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required.")
            .MaximumLength(50).WithMessage("Last name must be 50 characters or fewer.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Enter a valid email address.");

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Phone number is required.")
            .Matches(@"^\+?[\d\s\-(). ]{7,20}$").WithMessage("Enter a valid phone number.");

        RuleFor(x => x.DateOfBirth)
            .NotEmpty().WithMessage("Date of birth is required.")
            .Must(BeAPastDate).WithMessage("Date of birth must be a past date.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .Matches("[A-Z]").WithMessage("Password must contain at least 1 uppercase letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least 1 number.")
            .Matches("[^A-Za-z0-9]").WithMessage("Password must contain at least 1 special character.");

        RuleFor(x => x.AcceptedTerms)
            .Equal(true).WithMessage("You must accept the Terms of Service and Privacy Policy.");
    }

    private static bool BeAPastDate(string value)
        => DateOnly.TryParse(value, out var date) && date < DateOnly.FromDateTime(DateTime.UtcNow);
}
