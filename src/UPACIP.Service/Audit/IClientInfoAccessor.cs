namespace UPACIP.Service.Audit;

/// <summary>
/// Provides sanitised HTTP client metadata (IP address and User-Agent) for audit log entries
/// (US_064 AC-1, NFR-018).
/// </summary>
public interface IClientInfoAccessor
{
    /// <summary>
    /// Returns the client IP address for the current HTTP request.
    /// Supports <c>X-Forwarded-For</c> headers when behind an IIS reverse proxy.
    /// The value is sanitised to prevent header injection (OWASP A01, NFR-018).
    /// Returns an empty string when no HTTP context is available (e.g. background services).
    /// </summary>
    string GetClientIpAddress();

    /// <summary>
    /// Returns the <c>User-Agent</c> header value for the current HTTP request, truncated to
    /// 500 characters.
    /// Returns an empty string when no HTTP context is available.
    /// </summary>
    string GetUserAgent();
}
