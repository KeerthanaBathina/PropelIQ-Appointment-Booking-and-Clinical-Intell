namespace UPACIP.Service.Validation.Models;

/// <summary>
/// Configuration options for appointment booking and email validation rules
/// (US_085, DR-012, DR-014, edge cases 1 and 2).
///
/// Loaded via <c>IOptionsMonitor&lt;ValidationRuleOptions&gt;</c> so that changes to
/// <c>appsettings.json</c> take effect on the next validation request without restarting
/// the application (edge case 1: in-flight requests use the rules active at processing time).
/// </summary>
public sealed class ValidationRuleOptions
{
    /// <summary>Configuration section key in appsettings.json.</summary>
    public const string SectionName = "ValidationRules";

    /// <summary>
    /// IANA timezone ID (cross-platform) or Windows timezone ID for the clinic's local timezone.
    /// All appointment dates are normalized to this timezone before range validation,
    /// preventing incorrect rejections for bookings made near midnight (edge case 2).
    /// Default: "America/New_York".
    /// </summary>
    public string ClinicTimezoneId { get; set; } = "America/New_York";

    /// <summary>
    /// Maximum number of calendar days ahead that a booking is permitted.
    /// Enforces the advance-booking window per DR-012 and FR-013.
    /// Default: 90.
    /// </summary>
    public int MaxBookingDaysAhead { get; set; } = 90;

    /// <summary>
    /// .NET regex pattern used for email format validation.
    /// Updated values take effect on the next validation request without restart.
    /// Default: RFC 5321-compatible pattern.
    /// </summary>
    public string EmailRegexPattern { get; set; } =
        @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";
}
