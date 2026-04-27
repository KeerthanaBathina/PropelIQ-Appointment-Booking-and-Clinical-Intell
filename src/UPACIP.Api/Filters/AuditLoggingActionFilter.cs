using System.Security.Claims;
using Microsoft.AspNetCore.Mvc.Filters;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Audit;
using UPACIP.Service.Auth;

namespace UPACIP.Api.Filters;

/// <summary>
/// Cross-cutting action filter that automatically records audit log entries for
/// state-changing HTTP methods (US_064 — Implementation Plan step 6).
///
/// Usage:
///   Apply globally in Program.cs (<c>options.Filters.Add&lt;AuditLoggingActionFilter&gt;()</c>)
///   OR per-controller/action via <c>[ServiceFilter(typeof(AuditLoggingActionFilter))]</c>.
///
/// HTTP method → AuditAction mapping:
///   POST         → DataModify
///   PUT / PATCH  → DataModify
///   DELETE       → DataDelete
///   GET (default)→ not logged here; callers log DataAccess explicitly for sensitive endpoints.
///
/// The filter fires on <c>OnActionExecuted</c> (after the action completes) so that only
/// successful (2xx) responses generate audit entries — 4xx/5xx responses are skipped.
/// If the action result indicates a non-success status code, the entry is not written.
///
/// PII (IpAddress, UserAgent) is stored in the audit entry but MUST NOT appear in logs (NFR-017).
/// </summary>
public sealed class AuditLoggingActionFilter : IAsyncActionFilter
{
    private readonly IAuditLogService    _auditLogService;
    private readonly IClientInfoAccessor _clientInfo;

    public AuditLoggingActionFilter(
        IAuditLogService    auditLogService,
        IClientInfoAccessor clientInfo)
    {
        _auditLogService = auditLogService;
        _clientInfo      = clientInfo;
    }

    /// <inheritdoc/>
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var executed = await next();

        // Only log for state-changing HTTP methods on success (2xx).
        var method     = context.HttpContext.Request.Method.ToUpperInvariant();
        var statusCode = executed.HttpContext.Response.StatusCode;
        var isSuccess  = statusCode is >= 200 and < 300;

        if (!isSuccess)
            return;

        var action = method switch
        {
            "POST"   => AuditAction.DataModify,
            "PUT"    => AuditAction.DataModify,
            "PATCH"  => AuditAction.DataModify,
            "DELETE" => AuditAction.DataDelete,
            _        => (AuditAction?)null,
        };

        if (action is null)
            return;

        // Extract user identity.
        var userIdRaw = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? context.HttpContext.User.FindFirstValue("sub");
        Guid.TryParse(userIdRaw, out var userId);

        // Extract resource context from route values.
        var routeValues  = context.RouteData.Values;
        var resourceType = routeValues.TryGetValue("controller", out var ctrl)
            ? ctrl?.ToString() ?? "Unknown"
            : "Unknown";
        Guid? resourceId = null;
        if (routeValues.TryGetValue("id", out var rawId)
            && Guid.TryParse(rawId?.ToString(), out var parsedId))
        {
            resourceId = parsedId;
        }

        await _auditLogService.LogAsync(
            action:          action.Value,
            userId:          userId == Guid.Empty ? null : userId,
            resourceType:    resourceType,
            ipAddress:       _clientInfo.GetClientIpAddress(),
            userAgent:       _clientInfo.GetUserAgent(),
            resourceId:      resourceId,
            cancellationToken: context.HttpContext.RequestAborted);
    }
}
