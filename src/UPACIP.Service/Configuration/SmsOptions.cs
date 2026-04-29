namespace UPACIP.Service.Configuration;

/// <summary>
/// Strongly-typed options for outbound SMS delivery (US_101, AC-4).
/// Bound from the <c>Sms</c> section in <c>appsettings.json</c>.
///
/// This class captures the essential gateway-level SMS settings used for
/// centralized configuration management and startup validation.  It is intentionally
/// separate from <c>UPACIP.Service.Notifications.SmsProviderOptions</c>, which owns
/// the full Twilio transport configuration (E.164 validation, country-code allow-list,
/// retry policy, and SmsEnabled toggle).
///
/// Twilio credentials MUST be supplied via environment variables or user secrets (OWASP A07).
/// Never commit <c>AccountSid</c> or <c>AuthToken</c> to source control.
/// </summary>
public sealed class SmsOptions
{
    /// <summary>Configuration section name used for <c>IOptions&lt;T&gt;</c> binding.</summary>
    public const string SectionName = "Sms";

    /// <summary>Twilio Account SID (starts with <c>AC</c>).  Provided via env var or user secrets.</summary>
    public string AccountSid { get; init; } = string.Empty;

    /// <summary>Twilio Auth Token.  Provided via env var or user secrets — never log this value.</summary>
    public string AuthToken { get; init; } = string.Empty;

    /// <summary>Twilio-issued sender phone number in E.164 format (e.g. <c>+15005550006</c>).</summary>
    public string FromNumber { get; init; } = string.Empty;

    /// <summary>Maximum delivery attempts per SMS before the message is marked failed.  Default: 3.</summary>
    public int MaxRetries { get; init; } = 3;
}
