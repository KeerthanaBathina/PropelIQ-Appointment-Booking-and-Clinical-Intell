namespace UPACIP.Service.Configuration;

/// <summary>
/// Strongly-typed options for outbound email delivery (US_101, AC-4).
/// Bound from the <c>Email</c> section in <c>appsettings.json</c>.
///
/// This class captures the essential gateway-level email settings used for
/// centralized configuration management and startup validation.  It is intentionally
/// separate from <c>UPACIP.Service.Notifications.EmailProviderOptions</c>, which owns
/// the dual-provider SMTP transport configuration (primary + fallback, per-provider
/// credentials, and retry policy).
///
/// SMTP credentials MUST be supplied via environment variables or user secrets (OWASP A07).
/// </summary>
public sealed class EmailOptions
{
    /// <summary>Configuration section name used for <c>IOptions&lt;T&gt;</c> binding.</summary>
    public const string SectionName = "Email";

    /// <summary>SMTP server hostname or IP.  Example: <c>smtp.sendgrid.net</c>.</summary>
    public string SmtpHost { get; init; } = string.Empty;

    /// <summary>SMTP port.  Default: 587 (STARTTLS).</summary>
    public int SmtpPort { get; init; } = 587;

    /// <summary>Sender email address shown to recipients.  Provided via config or env var.</summary>
    public string FromAddress { get; init; } = string.Empty;

    /// <summary>Sender display name.  Default: <c>UPACIP Platform</c>.</summary>
    public string FromName { get; init; } = "UPACIP Platform";

    /// <summary>Whether TLS/SSL is required for SMTP delivery.  Default: <c>true</c>.</summary>
    public bool EnableSsl { get; init; } = true;
}
