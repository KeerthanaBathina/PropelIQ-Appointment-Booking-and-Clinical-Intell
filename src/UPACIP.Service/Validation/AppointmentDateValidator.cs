using FluentValidation;

namespace UPACIP.Service.Validation;

/// <summary>
/// Validates appointment date range per DR-012.
///
/// Rules:
///   - <see cref="CreateAppointmentRequest.AppointmentTime"/> must be after the current UTC time
///     (prevents booking in the past).
///   - <see cref="CreateAppointmentRequest.AppointmentTime"/> must be within 90 days from now
///     (prevents far-future bookings that cannot be operationally managed).
///
/// This validator runs before the request reaches the controller or the database,
/// eliminating an unnecessary round-trip for out-of-range dates.
/// </summary>
public sealed class AppointmentDateValidator : AbstractValidator<CreateAppointmentRequest>
{
    private const int MaxDaysAhead = 90;

    public AppointmentDateValidator()
    {
        RuleFor(x => x.PatientId)
            .NotEmpty()
            .WithMessage("PatientId is required.");

        RuleFor(x => x.AppointmentTime)
            .Must(BeInTheFuture)
                .WithMessage("Appointment date must be in the future.")
            .Must(BeWithin90Days)
                .WithMessage($"Appointment date must be within {MaxDaysAhead} days from today.");
    }

    private static bool BeInTheFuture(DateTimeOffset appointmentTime)
        => appointmentTime > DateTimeOffset.UtcNow;

    private static bool BeWithin90Days(DateTimeOffset appointmentTime)
        => appointmentTime <= DateTimeOffset.UtcNow.AddDays(MaxDaysAhead);
}
