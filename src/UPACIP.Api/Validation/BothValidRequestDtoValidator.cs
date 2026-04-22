using FluentValidation;
using UPACIP.Api.Models;

namespace UPACIP.Api.Validation;

/// <summary>
/// Validates <see cref="BothValidRequestDto"/> for
/// PUT /api/patients/{patientId}/conflicts/{conflictId}/both-valid (US_045, EC-2).
///
/// Rules:
///   - <see cref="BothValidRequestDto.Explanation"/> is required, at least 10 characters,
///     and must not exceed 2 000 characters.
/// </summary>
public sealed class BothValidRequestDtoValidator : AbstractValidator<BothValidRequestDto>
{
    private const int MinExplanationLength = 10;
    private const int MaxExplanationLength = 2_000;

    public BothValidRequestDtoValidator()
    {
        RuleFor(x => x.Explanation)
            .NotEmpty()
            .WithMessage("Explanation is required for BothValid conflict resolution.")
            .MinimumLength(MinExplanationLength)
            .WithMessage($"Explanation must be at least {MinExplanationLength} characters.")
            .MaximumLength(MaxExplanationLength)
            .WithMessage($"Explanation must not exceed {MaxExplanationLength} characters.");
    }
}
