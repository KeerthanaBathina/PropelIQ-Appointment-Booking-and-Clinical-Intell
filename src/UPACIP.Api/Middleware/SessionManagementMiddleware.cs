using System.Security.Claims;
using Microsoft.Extensions.Logging;
using UPACIP.Service.Auth;

namespace UPACIP.Api.Middleware;

/// <summary>
/// Per-request session activity tracker (AC-2, NFR-014).
///
/// For every authenticated request this middleware:
///   1. Extracts the <c>sub</c> (userId) claim from the validated JWT.
///   2. Calls <see cref="ISessionService.UpdateActivityAsync"/> to reset the Redis session
///      TTL to 15 minutes from now — implementing the sliding inactivity window.
///   3. Verifies the session still exists in Redis; if it has already expired, returns
///      401 Unauthorized so the client knows to re-authenticate (AC-1).
///
/// Unauthenticated endpoints (login, register, public routes) are skipped automatically
/// because <c>HttpContext.User.Identity.IsAuthenticated</c> will be false for them.
///
/// Redis unavailability: any exception from <see cref="ISessionService"/> is caught, logged,
/// and the request is allowed to continue (graceful degradation per NFR-023). This prevents
/// a Redis outage from locking all authenticated users out of the system.
///
/// Pipeline position: register AFTER <c>UseAuthentication</c> so <c>HttpContext.User</c>
/// is already populated when this middleware runs.
/// </summary>
public sealed class SessionManagementMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SessionManagementMiddleware> _logger;

    public SessionManagementMiddleware(RequestDelegate next, ILogger<SessionManagementMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ISessionService sessionService)
    {
        // Skip unauthenticated requests — login, register, public endpoints.
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            // Authenticated token without a sub claim — should never happen in practice.
            await _next(context);
            return;
        }

        try
        {
            var isActive = await sessionService.IsSessionActiveAsync(userId, context.RequestAborted);

            if (!isActive)
            {
                // Session has expired in Redis (15-min inactivity timeout reached — AC-1).
                _logger.LogInformation(
                    "Session expired for user {UserId}. Returning 401.", userId);

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(
                    "{\"message\":\"Session expired due to inactivity.\"}", context.RequestAborted);
                return;
            }

            // Reset the 15-minute sliding TTL (AC-2 — timer resets on activity).
            await sessionService.UpdateActivityAsync(userId, context.RequestAborted);
        }
        catch (Exception ex)
        {
            // Redis unavailable — log and allow the request to proceed (circuit breaker / NFR-023).
            _logger.LogWarning(
                ex,
                "SessionManagementMiddleware: Redis unavailable for user {UserId}. " +
                "Allowing request to continue (graceful degradation).",
                userId);
        }

        await _next(context);
    }
}

public static class SessionManagementMiddlewareExtensions
{
    /// <summary>
    /// Adds <see cref="SessionManagementMiddleware"/> to the pipeline.
    /// Must be called AFTER <c>UseAuthentication()</c> and BEFORE <c>UseAuthorization()</c>.
    /// </summary>
    public static IApplicationBuilder UseSessionManagement(this IApplicationBuilder app)
        => app.UseMiddleware<SessionManagementMiddleware>();
}
