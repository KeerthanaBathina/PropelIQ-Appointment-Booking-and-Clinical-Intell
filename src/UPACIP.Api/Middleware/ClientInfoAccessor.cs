using System.Net;
using Microsoft.AspNetCore.Http;
using UPACIP.Service.Audit;

namespace UPACIP.Api.Middleware;

/// <summary>
/// Extracts the client IP address and User-Agent from the current HTTP context (US_064 AC-1).
///
/// IP resolution order (supports IIS reverse proxy):
///   1. First valid IP from <c>X-Forwarded-For</c> header (after sanitisation).
///   2. <c>HttpContext.Connection.RemoteIpAddress</c>.
///
/// Security notes (OWASP A01, NFR-018):
///   - Only the FIRST token of X-Forwarded-For is used (leftmost = original client).
///   - The extracted value is validated as a well-formed IP address; invalid values are discarded.
///   - IPv6 loopback (::1) is normalised to 127.0.0.1 for consistency in audit records.
/// </summary>
public sealed class ClientInfoAccessor : IClientInfoAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public ClientInfoAccessor(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc/>
    public string GetClientIpAddress()
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx is null)
            return string.Empty;

        // Check X-Forwarded-For first (IIS ARR / reverse proxy scenarios).
        // Take only the first entry to prevent spoofing by appending extra IPs (OWASP A01).
        if (ctx.Request.Headers.TryGetValue("X-Forwarded-For", out var forwardedFor))
        {
            var firstEntry = forwardedFor.ToString().Split(',')[0].Trim();
            if (!string.IsNullOrEmpty(firstEntry) && IPAddress.TryParse(firstEntry, out _))
                return NormaliseIp(firstEntry);
        }

        var remoteIp = ctx.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        return NormaliseIp(remoteIp);
    }

    /// <inheritdoc/>
    public string GetUserAgent()
    {
        var ctx = _httpContextAccessor.HttpContext;
        if (ctx is null)
            return string.Empty;

        var ua = ctx.Request.Headers.UserAgent.ToString();
        return ua.Length > 500 ? ua[..500] : ua;
    }

    // Normalise IPv6 loopback to the standard IPv4 loopback representation.
    private static string NormaliseIp(string ip) =>
        ip == "::1" ? "127.0.0.1" : ip;
}
