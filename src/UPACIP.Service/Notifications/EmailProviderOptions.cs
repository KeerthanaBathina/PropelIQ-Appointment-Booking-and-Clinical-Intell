using System.ComponentModel.DataAnnotations;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Strongly-typed configuration model for the dual-provider SMTP transport layer.
/// Bound from the <c>EmailProvider</c> section in <c>appsettings.json</c> via
/// <c>builder.Services.Configure&lt;EmailProviderOptions&gt;(...)</c> in Program.cs.
///
/// Follows the same convention as <c>AiGatewaySettings</c> and <c>ClinicSettings</c>:
/// settings classes live in the Service project so that service implementations can
/// consume them via <c>IOptions&lt;T&gt;</c> without creating a circular project reference.
///
/// A <em>primary</em> provider (SendGrid) and an optional <em>fallback</em> provider
/// (Gmail SMTP) are configured independently so the transport switches without code
/// changes when the primary provider's quota or availability limits are exhausted (EC-1).
/// </summary>
public sealed class EmailProviderOptions
{
    /// <summary>Configuration section name used to bind this options class in appsettings.json.</summary>
    public const string SectionName = "EmailProvider";

    // -------------------------------------------------------------------------
    // Primary provider (SendGrid free-tier via SMTP relay)
    // -------------------------------------------------------------------------

    [Required]
    public SmtpProviderSettings Primary { get; init; } = new();

    // -------------------------------------------------------------------------
    // Fallback provider (Gmail SMTP — activated when primary quota is exceeded)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Optional fallback SMTP provider activated when <see cref="Primary"/> quota
    /// or availability limits are exceeded.  When <c>null</c> or not configured,
    /// failover is disabled and quota-exceeded conditions surface as a final failure.
    /// </summary>
    public SmtpProviderSettings? Fallback { get; init; }

    // -------------------------------------------------------------------------
    // Delivery policy
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maximum wall-clock time (seconds) allowed for a single send attempt,
    /// including all retries.  Enforces the 30-second delivery target (AC-1).
    /// Defaults to 30.
    /// </summary>
    [Range(5, 120)]
    public int DeliveryTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Number of retry attempts for transient failures (AC-3: backoff at 1s, 2s, 4s).
    /// Defaults to 3.
    /// </summary>
    [Range(1, 5)]
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>
    /// When <c>true</c>, the transport will attempt failover to <see cref="Fallback"/>
    /// when <see cref="Primary"/> exhausts its daily quota or becomes unavailable (EC-1).
    /// Defaults to <c>true</c>.
    /// </summary>
    public bool EnableFallback { get; init; } = true;
}

/// <summary>
/// Per-provider SMTP connection and sender-identity settings.
/// Credentials MUST be supplied via user secrets or environment variables;
/// the placeholder values in <c>appsettings.json</c> are never committed with real values.
/// </summary>
public sealed class SmtpProviderSettings
{
    /// <summary>
    /// Human-readable provider label used in diagnostic logs.
    /// Example: <c>"SendGrid"</c>, <c>"Gmail"</c>.
    /// Never logged alongside credentials.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string ProviderName { get; init; } = string.Empty;

    /// <summary>
    /// SMTP relay hostname.
    /// Examples: <c>smtp.sendgrid.net</c>, <c>smtp.gmail.com</c>.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string Host { get; init; } = string.Empty;

    /// <summary>
    /// TCP port. Common values: 587 (STARTTLS), 465 (SSL/TLS).
    /// </summary>
    [Range(1, 65535)]
    public int Port { get; init; } = 587;

    /// <summary>
    /// SMTP authentication username.  For SendGrid this is the literal string <c>"apikey"</c>;
    /// for Gmail it is the full Gmail address.
    /// MUST be supplied via user secrets; never committed to source control.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// SMTP authentication password / API key.
    /// MUST be supplied via user secrets; NEVER committed to source control (OWASP A02).
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string Password { get; init; } = string.Empty;

    /// <summary>RFC 5321 envelope-from address (e.g. <c>noreply@upacip.clinic</c>).</summary>
    [Required(AllowEmptyStrings = false)]
    [EmailAddress]
    public string FromAddress { get; init; } = string.Empty;

    /// <summary>Display name shown in the recipient's email client (e.g. <c>"UPACIP Medical"</c>).</summary>
    [Required(AllowEmptyStrings = false)]
    public string FromName { get; init; } = "UPACIP";

    /// <summary>
    /// Approximate daily send quota for this provider (SendGrid free tier = 100/day).
    /// Used for diagnostic logging only; actual enforcement is by the provider.
    /// Set to <c>0</c> to indicate no known quota (unlimited / paid tier).
    /// </summary>
    [Range(0, int.MaxValue)]
    public int DailyQuotaLimit { get; init; } = 100;
}
