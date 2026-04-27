using FluentValidation;

namespace UPACIP.Service.Admin;

/// <summary>
/// Validates individual <see cref="BusinessHoursEntryDto"/> records.
/// Rules:
///   - DayOfWeek: 0–6.
///   - If IsClosed = false: OpenTime and CloseTime must be provided and OpenTime &lt; CloseTime.
/// </summary>
public sealed class BusinessHoursEntryDtoValidator : AbstractValidator<BusinessHoursEntryDto>
{
    public BusinessHoursEntryDtoValidator()
    {
        RuleFor(x => x.DayOfWeek)
            .InclusiveBetween(0, 6)
            .WithMessage("DayOfWeek must be between 0 (Sunday) and 6 (Saturday).");

        When(x => !x.IsClosed, () =>
        {
            RuleFor(x => x.OpenTime)
                .NotNull()
                .WithMessage("OpenTime is required when the day is not closed.");

            RuleFor(x => x.CloseTime)
                .NotNull()
                .WithMessage("CloseTime is required when the day is not closed.");

            RuleFor(x => x.CloseTime)
                .GreaterThan(x => x.OpenTime)
                .When(x => x.OpenTime.HasValue && x.CloseTime.HasValue)
                .WithMessage("CloseTime must be after OpenTime.");
        });
    }
}

/// <summary>
/// Validates <see cref="UpdateBusinessHoursRequest"/> for
/// PUT /api/admin/config/business-hours (US_059 AC-3).
///
/// Rules:
///   - Entries: required, not empty, no more than 7.
///   - Each entry individually valid.
///   - No duplicate DayOfWeek values in one request.
/// </summary>
public sealed class UpdateBusinessHoursRequestValidator
    : AbstractValidator<UpdateBusinessHoursRequest>
{
    public UpdateBusinessHoursRequestValidator()
    {
        RuleFor(x => x.Entries)
            .NotNull()
            .WithMessage("Entries must be provided.")
            .NotEmpty()
            .WithMessage("At least one entry is required.")
            .Must(e => e.Count <= 7)
            .WithMessage("Cannot update more than 7 days at once.");

        RuleForEach(x => x.Entries)
            .SetValidator(new BusinessHoursEntryDtoValidator());

        RuleFor(x => x.Entries)
            .Must(entries => entries
                .Select(e => e.DayOfWeek)
                .Distinct()
                .Count() == entries.Count)
            .WithMessage("Duplicate DayOfWeek values are not allowed in a single request.");
    }
}

/// <summary>
/// Validates <see cref="CreateHolidayRequest"/> for
/// POST /api/admin/config/holidays (US_059 AC-4).
///
/// Rules:
///   - Name: required, max 200 characters.
///   - Date: must not be in the past for non-recurring holidays
///     (recurring holidays may be created with historical dates for setup purposes).
/// </summary>
public sealed class CreateHolidayRequestValidator : AbstractValidator<CreateHolidayRequest>
{
    public CreateHolidayRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .WithMessage("Holiday name is required.")
            .MaximumLength(200)
            .WithMessage("Holiday name must not exceed 200 characters.");

        // Past dates allowed for recurring holidays (e.g. Christmas = Dec 25 every year).
        When(x => !x.IsRecurring, () =>
        {
            RuleFor(x => x.Date)
                .Must(d => d >= DateOnly.FromDateTime(DateTime.UtcNow))
                .WithMessage("Non-recurring holiday date must not be in the past.");
        });
    }
}
