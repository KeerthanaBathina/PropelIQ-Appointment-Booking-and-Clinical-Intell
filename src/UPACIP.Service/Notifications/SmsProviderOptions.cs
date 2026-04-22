using System.ComponentModel.DataAnnotations;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Strongly-typed configuration model for the Twilio SMS transport layer.
/// Bound from the <c>SmsProvider</c> section in <c>appsettings.json</c> via
/// <c>builder.Services.Configure&lt;SmsProviderOptions&gt;(...)</c> in Program.cs.
///
/// Follows the same placement convention as <c>EmailProviderOptions</c>:
/// settings classes live in the Service project to avoid circular references.
///
/// All credential fields must be populated via user secrets or environment variables.
/// The placeholder values in appsettings.json are never valid for live delivery.
/// </summary>
public sealed class SmsProviderOptions
{
    /// <summary>Configuration section name used to bind this options class in appsettings.json.</summary>
    public const string SectionName = "SmsProvider";

    // -------------------------------------------------------------------------
    // Twilio credentials (MUST be supplied via user secrets — OWASP A02)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Twilio Account SID (starts with <c>AC</c>).
    /// MUST be supplied via user secrets; never committed to source control.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string AccountSid { get; init; } = string.Empty;

    /// <summary>
    /// Twilio Auth Token.
    /// MUST be supplied via user secrets; NEVER committed to source control (OWASP A02).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string AuthToken { get; init; } = string.Empty;

    /// <summary>
    /// Twilio-issued sender phone number in E.164 format (e.g. <c>+15005550006</c>).
    /// Must be a US number for Phase 1.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string FromNumber { get; init; } = string.Empty;

    // -------------------------------------------------------------------------
    // Delivery policy
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maximum wall-clock time (seconds) allowed for a single SMS send attempt,
    /// including all retries.  Enforces the 30-second delivery target (AC-1).
    /// Defaults to 30.
    /// </summary>
    [Range(5, 120)]
    public int DeliveryTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Number of retry attempts for transient Twilio API failures
    /// (AC-3: backoff at 1s, 2s, 4s).  Defaults to 3.
    /// </summary>
    [Range(1, 5)]
    public int MaxRetryAttempts { get; init; } = 3;

    // -------------------------------------------------------------------------
    // Operational flags
    // -------------------------------------------------------------------------

    /// <summary>
    /// When <c>false</c>, all SMS send attempts are skipped and a
    /// <see cref="SmsDeliveryOutcome.GatewayDisabled"/> result is returned immediately.
    /// Set to <c>false</c> at runtime when Twilio trial credits are exhausted (EC-1)
    /// so the system continues with email-only notifications without redeployment.
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool SmsEnabled { get; init; } = true;

    // -------------------------------------------------------------------------
    // Phase 1 scope restriction
    // -------------------------------------------------------------------------

    /// <summary>
    /// E.164 country-code prefix accepted in Phase 1.
    /// Only US numbers (<c>+1</c>) are supported; international numbers are
    /// rejected with a deterministic validation error (EC-2).
    /// </summary>
    public string AllowedCountryCode { get; init; } = "+1";
}
