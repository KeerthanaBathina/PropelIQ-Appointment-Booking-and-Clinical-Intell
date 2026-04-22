using FluentValidation;
using UPACIP.Service.Consolidation;

namespace UPACIP.Api.Validation;

/// <summary>
/// Validates <see cref="ManualVerifyRequestDto"/> submitted to
/// <c>POST /api/patients/{id}/profile/manual-verify</c> (US_046 AC-3, NFR-018).
/// </summary>
public sealed class ManualVerifyRequestDtoValidator : AbstractValidator<ManualVerifyRequestDto>
{
    public ManualVerifyRequestDtoValidator()
    {
        RuleFor(r => r.Entries)
            .NotEmpty()
            .WithMessage("At least one entry is required.")
            .Must(e => e.Count <= 200)
            .WithMessage("A single batch may not exceed 200 entries.");

        RuleForEach(r => r.Entries).ChildRules(entry =>
        {
            entry.RuleFor(e => e.ExtractedDataId)
                .NotEmpty()
                .WithMessage("ExtractedDataId is required.");

            entry.RuleFor(e => e.CorrectedValue)
                .MaximumLength(500)
                .When(e => e.CorrectedValue is not null)
                .WithMessage("CorrectedValue must not exceed 500 characters.");

            // Sanitize free-text to prevent injection (NFR-018).
            entry.RuleFor(e => e.ResolutionNotes)
                .NotEmpty()
                .WithMessage("ResolutionNotes is required for audit attribution.")
                .MaximumLength(2000)
                .WithMessage("ResolutionNotes must not exceed 2000 characters.")
                .Must(n => !ContainsDangerousCharacters(n))
                .WithMessage("ResolutionNotes contains disallowed characters.");
        });
    }

    private static bool ContainsDangerousCharacters(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        // Reject SQL injection markers and script injection patterns (NFR-018).
        return value.Contains("--", StringComparison.Ordinal)
            || value.Contains("/*", StringComparison.Ordinal)
            || value.Contains("<script", StringComparison.OrdinalIgnoreCase)
            || value.Contains("javascript:", StringComparison.OrdinalIgnoreCase);
    }
}
