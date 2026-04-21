using FluentValidation;
using UPACIP.Service.Appointments;

namespace UPACIP.Service.Validation;

/// <summary>
/// Validates <see cref="BookingRequest"/> for POST /api/appointments (US_018).
///
/// Rules (FR-013, EC-2):
///   - SlotId: required, not empty.
///   - ProviderId: required, non-empty Guid.
///   - AppointmentType: required, ≤ 50 characters.
///   - AppointmentTime: must be in the future (prevents backdating).
///   - AppointmentTime: must be ≤ today + 90 days (90-day advance-booking window).
///     Slot at exactly 90 days is accepted; 91+ days is rejected with a specific error (EC-2).
///
/// This validator runs automatically before the controller action executes via
/// FluentValidation.AspNetCore auto-validation. Invalid requests receive 400 with
/// <see cref="UPACIP.Api.Models.ErrorResponse"/> (consistent with all other endpoints).
/// </summary>
public sealed class BookingRequestValidator : AbstractValidator<BookingRequest>
{
    private const int MaxDaysAhead = 90;

    public BookingRequestValidator()
    {
        RuleFor(x => x.SlotId)
            .NotEmpty()
            .WithMessage("SlotId is required.");

        RuleFor(x => x.ProviderId)
            .NotEmpty()
            .WithMessage("ProviderId is required.");

        RuleFor(x => x.AppointmentType)
            .NotEmpty()
            .WithMessage("AppointmentType is required.")
            .MaximumLength(50)
            .WithMessage("AppointmentType must not exceed 50 characters.");

        RuleFor(x => x.AppointmentTime)
            .Must(t => t > DateTime.UtcNow)
            .WithMessage("AppointmentTime must be in the future.")
            .Must(BeWithin90Days)
            .WithMessage(
                $"Appointment date exceeds maximum {MaxDaysAhead}-day advance booking window (FR-013).");
    }

    // Slot at exactly 90 days from today midnight (UTC) is permitted (EC-2 boundary rule).
    private static bool BeWithin90Days(DateTime appointmentTime)
        => appointmentTime.Date <= DateTime.UtcNow.Date.AddDays(MaxDaysAhead);
}
