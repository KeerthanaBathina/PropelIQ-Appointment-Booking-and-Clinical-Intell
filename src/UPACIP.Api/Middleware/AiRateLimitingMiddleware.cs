using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using UPACIP.Service.AiSafety;

namespace UPACIP.Api.Middleware;

/// <summary>
/// ASP.NET Core convention-based middleware that enforces per-user sliding-window
/// rate limits on AI endpoints (US_079 task_003, AC-4, AIR-S08, TR-027).
///
/// <para>
/// <b>Route matching</b> — applies to requests whose path starts with:
/// <list type="bullet">
///   <item><c>/api/ai/</c></item>
///   <item><c>/api/intake/</c></item>
///   <item><c>/api/coding/</c></item>
/// </list>
/// All other routes are passed through unchanged.
/// </para>
///
/// <para>
/// <b>Pipeline position</b> — registered <em>after</em> <c>UseAuthentication</c> and
/// <c>UseAuthorization</c> so that <see cref="HttpContext.User"/> is fully populated
/// with JWT claims before limit resolution.
/// </para>
///
/// <para>
/// <b>Unauthenticated requests</b> — rejected with HTTP 401 before the rate check runs;
/// rate limiting should never be a substitute for authentication.
/// </para>
///
/// <para>
/// <b>429 response body</b>: <c>application/json</c> —
/// <c>{ "error": "Rate limit exceeded", "retryAfterSeconds": N, "limit": L }</c>
/// </para>
///
/// <para>
/// <b>IAiRateLimiter</b> is Scoped and injected via ASP.NET Core's method-injection
/// pattern on <see cref="InvokeAsync"/> — no service-locator pattern required.
/// </para>
/// </summary>
public sealed class AiRateLimitingMiddleware
{
    // Route prefixes subject to AI rate limiting (lowercase for case-insensitive comparison).
    private static readonly string[] ProtectedPrefixes =
    [
        "/api/ai/",
        "/api/intake/",
        "/api/coding/",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented        = false,
    };

    private readonly RequestDelegate                    _next;
    private readonly ILogger<AiRateLimitingMiddleware>  _logger;

    public AiRateLimitingMiddleware(
        RequestDelegate                   next,
        ILogger<AiRateLimitingMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    /// <summary>
    /// Per-request invocation.  <paramref name="rateLimiter"/> is resolved from the
    /// DI container's request scope via ASP.NET Core method injection.
    /// </summary>
    public async Task InvokeAsync(HttpContext context, IAiRateLimiter rateLimiter)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Only apply to protected AI/intake/coding routes.
        if (!IsProtectedPath(path))
        {
            await _next(context);
            return;
        }

        // Ensure the user is authenticated before checking the limit.
        if (context.User?.Identity?.IsAuthenticated != true)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var userId   = context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
        var userRole = context.User.FindFirstValue(ClaimTypes.Role)           ?? string.Empty;

        if (string.IsNullOrEmpty(userId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var result = await rateLimiter.CheckRateLimitAsync(userId, userRole, context.RequestAborted);

        // Always add quota headers (RFC 6585 / draft-ietf-httpapi-ratelimit-headers).
        var resetTimestamp = DateTimeOffset.UtcNow
            .AddSeconds(result.RetryAfterSeconds > 0 ? result.RetryAfterSeconds : 0)
            .ToUnixTimeSeconds();

        context.Response.Headers["X-RateLimit-Limit"]     = result.AppliedLimit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = result.RemainingRequests.ToString();
        context.Response.Headers["X-RateLimit-Reset"]     = resetTimestamp.ToString();

        if (result.IsAllowed)
        {
            await _next(context);
            return;
        }

        // Rate limit exceeded — return 429 with Retry-After (RFC 6585 §4).
        _logger.LogWarning(
            "AiRateLimitingMiddleware: rate limit exceeded. " +
            "UserId={UserId} Role={Role} Limit={Limit} Count={Count} " +
            "RetryAfterSeconds={RetryAfter} Path={Path}",
            userId, userRole, result.AppliedLimit, result.CurrentCount,
            result.RetryAfterSeconds, path);

        context.Response.StatusCode                         = StatusCodes.Status429TooManyRequests;
        context.Response.Headers["Retry-After"]             = result.RetryAfterSeconds.ToString();
        context.Response.ContentType                        = "application/json";

        var body = JsonSerializer.Serialize(new
        {
            error             = "Rate limit exceeded",
            retryAfterSeconds = result.RetryAfterSeconds,
            limit             = result.AppliedLimit,
        }, JsonOptions);

        await context.Response.WriteAsync(body, context.RequestAborted);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static bool IsProtectedPath(string path)
    {
        foreach (var prefix in ProtectedPrefixes)
        {
            if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
