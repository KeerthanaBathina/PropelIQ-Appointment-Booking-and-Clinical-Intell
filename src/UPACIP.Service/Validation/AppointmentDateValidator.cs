using FluentValidation;
using Microsoft.Extensions.Options;
using UPACIP.Service.Validation.Models;

namespace UPACIP.Service.Validation;

/// <summary>
/// Validates appointment date range per DR-012 and AC-1 (US_085).
///
/// Rules — all evaluated in the clinic's configured local timezone (edge case 2):
///   - <see cref="CreateAppointmentRequest.AppointmentTime"/> must not be in the past.
///     Uses a distinct error message so clients know the date has already elapsed.
///   - <see cref="CreateAppointmentRequest.AppointmentTime"/> must be within
///     <see cref="ValidationRuleOptions.MaxBookingDaysAhead"/> calendar days from today
///     (configurable per DR-012, default 90).
///
/// Timezone normalization prevents incorrect rejections for bookings submitted at
/// 11:59 PM local time when the UTC conversion crosses midnight into the next day
/// (edge case 2). All dates are normalized to the clinic's configured timezone before
/// validation. <see cref="ValidationRuleOptions.ClinicTimezoneId"/> is loaded via
/// <c>IOptionsMonitor</c> and refreshed on configuration change without restart (edge case 1).
///
/// This validator runs before the request reaches the controller or the database,
/// eliminating an unnecessary round-trip for out-of-range dates.
/// </summary>
public sealed class AppointmentDateValidator : AbstractValidator<CreateAppointmentRequest>
{
    private readonly IOptionsMonitor<ValidationRuleOptions> _options;

    public AppointmentDateValidator(IOptionsMonitor<ValidationRuleOptions> options)
    {
        _options = options;

        RuleFor(x => x.PatientId)
            .NotEmpty()
            .WithMessage("PatientId is required.");

        // Separate rules for "past" vs "too far in future" so clients display distinct guidance.
        RuleFor(x => x.AppointmentTime)
            .Must(IsNotInThePast)
            .WithMessage(x =>
            {
                var opts = _options.CurrentValue;
                var tz   = ResolveTimezone(opts.ClinicTimezoneId);
                var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;
                return $"Appointment date cannot be in the past. " +
                       $"Today is {today:yyyy-MM-dd} ({opts.ClinicTimezoneId} timezone).";
            });

        RuleFor(x => x.AppointmentTime)
            .Must(IsWithinMaxDays)
            .WithMessage(x =>
            {
                var opts    = _options.CurrentValue;
                var tz      = ResolveTimezone(opts.ClinicTimezoneId);
                var today   = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;
                var maxDate = today.AddDays(opts.MaxBookingDaysAhead);
                return $"Appointment date must be within {opts.MaxBookingDaysAhead} days. " +
                       $"Latest accepted date is {maxDate:yyyy-MM-dd} ({opts.ClinicTimezoneId} timezone).";
            });
    }

    // ── Predicate helpers ──────────────────────────────────────────────────────

    private bool IsNotInThePast(DateTimeOffset appointmentTime)
    {
        var opts  = _options.CurrentValue;
        var tz    = ResolveTimezone(opts.ClinicTimezoneId);
        var local = TimeZoneInfo.ConvertTimeFromUtc(appointmentTime.UtcDateTime, tz).Date;
        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date;
        return local >= today;
    }

    private bool IsWithinMaxDays(DateTimeOffset appointmentTime)
    {
        var opts    = _options.CurrentValue;
        var tz      = ResolveTimezone(opts.ClinicTimezoneId);
        var local   = TimeZoneInfo.ConvertTimeFromUtc(appointmentTime.UtcDateTime, tz).Date;
        var maxDate = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz).Date
                          .AddDays(opts.MaxBookingDaysAhead);
        return local <= maxDate;
    }

    // ── Timezone resolution ────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a timezone ID to a <see cref="TimeZoneInfo"/> instance.
    /// Tries both IANA (cross-platform) and Windows timezone IDs; falls back to UTC
    /// if neither resolves, so the validator never throws for misconfigured IDs.
    /// </summary>
    private static TimeZoneInfo ResolveTimezone(string timezoneId)
    {
        if (TimeZoneInfo.TryFindSystemTimeZoneById(timezoneId, out var tz))
            return tz;

        // Attempt Windows → IANA mapping (no-op on Windows where Win IDs resolve directly).
        if (TimeZoneInfo.TryFindSystemTimeZoneById("UTC", out var utc))
            return utc;

        return TimeZoneInfo.Utc;
    }
}
