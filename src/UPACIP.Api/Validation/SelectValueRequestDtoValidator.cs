using FluentValidation;
using UPACIP.Api.Models;

namespace UPACIP.Api.Validation;

/// <summary>
/// Validates <see cref="SelectValueRequestDto"/> for
/// PUT /api/patients/{patientId}/conflicts/{conflictId}/select-value (US_045, AC-2).
///
/// Rules:
///   - <see cref="SelectValueRequestDto.SelectedExtractedDataId"/> must be a non-empty GUID.
///   - <see cref="SelectValueRequestDto.ResolutionNotes"/> is required and must not exceed 2 000 characters.
/// </summary>
public sealed class SelectValueRequestDtoValidator : AbstractValidator<SelectValueRequestDto>
{
    private const int MaxNotesLength = 2_000;

    public SelectValueRequestDtoValidator()
    {
        RuleFor(x => x.SelectedExtractedDataId)
            .NotEmpty()
            .WithMessage("SelectedExtractedDataId is required and must be a valid non-empty GUID.");

        RuleFor(x => x.ResolutionNotes)
            .NotEmpty()
            .WithMessage("ResolutionNotes are required for the resolution audit trail.")
            .MaximumLength(MaxNotesLength)
            .WithMessage($"ResolutionNotes must not exceed {MaxNotesLength} characters.");
    }
}
