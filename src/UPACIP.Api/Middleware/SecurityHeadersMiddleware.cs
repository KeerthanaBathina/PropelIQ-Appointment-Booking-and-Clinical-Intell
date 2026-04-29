using Microsoft.Extensions.Options;
using UPACIP.Service.Security.Models;

namespace UPACIP.Api.Middleware;

/// <summary>
/// Middleware that adds OWASP-recommended security response headers to every HTTP response
/// (US_093 task_002, AC-3, TR-018, NFR-018).
///
/// Headers applied:
///   Content-Security-Policy    — Restricts script execution context (primary XSS defense, OWASP A03).
///   X-Content-Type-Options     — Prevents MIME-type sniffing (OWASP A05).
///   X-Frame-Options            — Prevents clickjacking by blocking framing (OWASP A05).
///   X-XSS-Protection           — Disabled (0) — CSP is the modern replacement; legacy filter causes issues.
///   Referrer-Policy            — Limits referrer leakage to same-origin (OWASP A02 data exposure).
///   Permissions-Policy         — Restricts browser feature access (camera, microphone, geolocation).
///   Strict-Transport-Security  — Enforces HTTPS with 1-year max-age + subdomains (TR-018, AC-3).
///
/// Placement: registered before UseHsts / UseHttpsRedirection so headers apply to all
/// responses including redirects and error pages.
/// Register via <see cref="UseSecurityHeadersExtension.UseSecurityHeaders"/>.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate                  _next;
    private readonly IOptionsMonitor<SecurityOptions> _options;
    private readonly ILogger<SecurityHeadersMiddleware> _logger;

    // Static headers that never change — set once at construction time.
    private static readonly (string Name, string Value)[] StaticHeaders =
    {
        ("X-Content-Type-Options",  "nosniff"),
        ("X-Frame-Options",         "DENY"),
        ("X-XSS-Protection",        "0"),
        ("Referrer-Policy",         "strict-origin-when-cross-origin"),
        ("Permissions-Policy",      "camera=(), microphone=(), geolocation=()"),
        ("Strict-Transport-Security", "max-age=31536000; includeSubDomains"),
    };

    public SecurityHeadersMiddleware(
        RequestDelegate                    next,
        IOptionsMonitor<SecurityOptions>   options,
        ILogger<SecurityHeadersMiddleware> logger)
    {
        _next    = next;
        _options = options;
        _logger  = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Register a callback to add headers before the response starts.
        // Using OnStarting ensures headers are set even for error responses.
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            // Dynamic CSP from SecurityOptions (supports hot-reload)
            var csp = _options.CurrentValue.ContentSecurityPolicy;
            if (!string.IsNullOrWhiteSpace(csp))
                headers["Content-Security-Policy"] = csp;

            // Static headers
            foreach (var (name, value) in StaticHeaders)
                headers[name] = value;

            return Task.CompletedTask;
        });

        await _next(context);
    }
}

/// <summary>Extension method for clean pipeline registration.</summary>
public static class UseSecurityHeadersExtension
{
    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
        => app.UseMiddleware<SecurityHeadersMiddleware>();
}
