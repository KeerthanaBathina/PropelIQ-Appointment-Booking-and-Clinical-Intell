namespace UPACIP.Service.Security.Models;

/// <summary>
/// Configuration for the input sanitization and security headers pipeline
/// (US_093 task_002, AC-3, NFR-018, OWASP A03 — Injection).
///
/// Primary SQL injection defense: EF Core parameterized queries (NFR-018).
/// This configuration controls the secondary application-level detection layer.
///
/// Bound from the <c>"InputSanitization"</c> section in <c>appsettings.json</c>.
/// Hot-reloaded via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/>.
/// </summary>
public sealed class SecurityOptions
{
    /// <summary>Configuration section key in appsettings.json.</summary>
    public const string SectionName = "InputSanitization";

    /// <summary>
    /// Maximum allowed request body size in bytes.
    /// Requests exceeding this limit are rejected with 400 Bad Request to prevent
    /// buffer overflow and denial-of-service attacks (OWASP A05).
    /// Default: 1 048 576 bytes (1 MB).
    /// </summary>
    public int MaxRequestBodySizeBytes { get; set; } = 1_048_576;

    /// <summary>
    /// Maximum length for any single string input field.
    /// Values longer than this are truncated during sanitization to prevent
    /// excessively large payloads reaching business logic.
    /// Default: 10 000 characters.
    /// </summary>
    public int MaxStringInputLength { get; set; } = 10_000;

    /// <summary>
    /// When <c>true</c>, the secondary input sanitization detection layer is active.
    /// Set to <c>false</c> in development only. Default: <c>true</c>.
    /// </summary>
    public bool EnableInputSanitization { get; set; } = true;

    /// <summary>
    /// When <c>true</c>, blocked requests are logged with a <c>SECURITY_THREAT_BLOCKED</c>
    /// structured event and correlation ID for incident investigation (AC-3).
    /// Default: <c>true</c>.
    /// </summary>
    public bool LogBlockedRequests { get; set; } = true;

    /// <summary>
    /// Content Security Policy header value applied to all API responses.
    /// Restricts script execution context to prevent inline XSS (OWASP A03).
    /// Default: a strict policy allowing only same-origin content.
    /// </summary>
    public string ContentSecurityPolicy { get; set; } =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; font-src 'self'; object-src 'none'; frame-ancestors 'none'";
}
