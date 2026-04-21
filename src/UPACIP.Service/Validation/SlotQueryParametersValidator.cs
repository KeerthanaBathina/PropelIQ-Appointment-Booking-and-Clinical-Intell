using FluentValidation;
using UPACIP.Service.Appointments;

namespace UPACIP.Service.Validation;

/// <summary>
/// Validates <see cref="SlotQueryParameters"/> for the appointment slot availability endpoint.
///
/// Rules (FR-013, EC-2):
///   - <see cref="SlotQueryParameters.StartDate"/> must be today or in the future.
///   - Resolved end date must be ≥ start date.
///   - Resolved end date must be ≤ today + 90 days (90-day advance-booking window).
///   - <see cref="SlotQueryParameters.AppointmentType"/> must be ≤ 50 characters when provided.
/// </summary>
public sealed class SlotQueryParametersValidator : AbstractValidator<SlotQueryParameters>
{
    private const int MaxDaysAhead = 90;

    public SlotQueryParametersValidator()
    {
        RuleFor(x => x.StartDate)
            .Must(d => d >= DateOnly.FromDateTime(DateTime.UtcNow.Date))
            .WithMessage("StartDate must be today or a future date.");

        RuleFor(x => x.ResolvedEndDate)
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("EndDate must be on or after StartDate.")
            .Must(d => d <= DateOnly.FromDateTime(DateTime.UtcNow.Date.AddDays(MaxDaysAhead)))
            .WithMessage($"EndDate must be within {MaxDaysAhead} days from today (FR-013).");

        When(x => x.AppointmentType is not null, () =>
        {
            RuleFor(x => x.AppointmentType!)
                .MaximumLength(50)
                .WithMessage("AppointmentType must not exceed 50 characters.");
        });
    }
}
