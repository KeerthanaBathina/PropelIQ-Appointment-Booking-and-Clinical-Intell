using System.Security.Claims;
using System.Text.Json;
using UPACIP.Api.Middleware;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Api.Authorization;

/// <summary>
/// Static helpers used by <see cref="Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents"/>
/// to write structured 401/403 JSON responses and persist authorization events to the
/// audit trail (NFR-012, NFR-035).
///
/// Keeping the logic here (rather than inline in Program.cs) ensures the structured
/// response shape and audit logging are consistent across all auth failure paths.
/// </summary>
public static class AuthorizationResultHandler
{
    // ─── Response bodies ──────────────────────────────────────────────────────

    public static object ForbiddenBody(string correlationId) => new
    {
        error         = "Forbidden",
        message       = "You do not have permission to access this resource.",
        correlationId,
    };

    public static object UnauthorizedBody(string correlationId) => new
    {
        error         = "Unauthorized",
        message       = "Authentication required. Please sign in.",
        correlationId,
    };

    // ─── Structured logging + DB audit ────────────────────────────────────────

    /// <summary>
    /// Logs a 403 Forbidden event and, when the user is identified, writes an
    /// <see cref="AuditLog"/> record to the database (NFR-012).
    /// </summary>
    public static async Task HandleForbiddenAsync(
        HttpContext context,
        ILogger logger,
        IServiceScopeFactory scopeFactory)
    {
        var correlationId = context.Items[CorrelationIdMiddleware.ItemsKey]?.ToString()
                            ?? Guid.NewGuid().ToString();

        var userId = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var role   = context.User.FindFirst(ClaimTypes.Role)?.Value ?? "unknown";
        var path   = context.Request.Path.Value ?? string.Empty;
        var ip     = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var ua     = context.Request.Headers["User-Agent"].FirstOrDefault() ?? string.Empty;

        logger.LogWarning(
            "Authorization denied. CorrelationId={CorrelationId} UserId={UserId} " +
            "Role={Role} Path={Path} IP={IpAddress}",
            correlationId, userId, role, path, ip);

        if (Guid.TryParse(userId, out var userGuid))
            await WriteAuditLogAsync(userGuid, AuditAction.AccessDenied, path, ip, ua, logger, scopeFactory);

        context.Response.StatusCode  = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(ForbiddenBody(correlationId)));
    }

    /// <summary>
    /// Logs a 401 Unauthorized event via Serilog (no user ID available for DB audit).
    /// </summary>
    public static async Task HandleChallengedAsync(
        HttpContext context,
        ILogger logger)
    {
        var correlationId = context.Items[CorrelationIdMiddleware.ItemsKey]?.ToString()
                            ?? Guid.NewGuid().ToString();

        var ip = context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var ua = context.Request.Headers["User-Agent"].FirstOrDefault() ?? string.Empty;

        logger.LogWarning(
            "Authentication required. CorrelationId={CorrelationId} IP={IpAddress} " +
            "UserAgent={UserAgent} Path={Path}",
            correlationId, ip, ua, context.Request.Path.Value);

        context.Response.StatusCode  = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(UnauthorizedBody(correlationId)));
    }

    // ─── DB helper ────────────────────────────────────────────────────────────

    private static async Task WriteAuditLogAsync(
        Guid              userId,
        AuditAction       action,
        string            resourceType,
        string            ipAddress,
        string            userAgent,
        ILogger           logger,
        IServiceScopeFactory scopeFactory)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            db.AuditLogs.Add(new AuditLog
            {
                UserId       = userId,
                Action       = action,
                ResourceType = resourceType,
                IpAddress    = ipAddress,
                UserAgent    = userAgent,
            });
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to write authorization audit log for user {UserId}.", userId);
        }
    }
}

