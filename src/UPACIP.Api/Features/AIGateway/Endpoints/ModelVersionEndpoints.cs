using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using UPACIP.Api.Authorization;
using UPACIP.Api.Features.AIGateway.Versioning;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.Auth;

namespace UPACIP.Api.Features.AIGateway.Endpoints;

/// <summary>
/// ASP.NET Core Minimal API endpoints for AI Gateway model version management
/// (US_069 TASK_003, AIR-O05).
///
/// <para>Endpoints:</para>
/// <list type="bullet">
///   <item><c>GET  /api/admin/ai-gateway/versions</c> — current active and previous model versions per provider.</item>
///   <item><c>POST /api/admin/ai-gateway/versions/rollback</c> — trigger rollback to previous version for a given provider.</item>
/// </list>
///
/// Both endpoints require the <see cref="RbacPolicies.AdminOnly"/> authorization policy.
/// The rollback endpoint writes a structured audit entry to the <c>AuditLog</c> table via
/// <see cref="IAuditLogService"/> and fires a structured Serilog event for operational monitoring.
/// </summary>
public static class ModelVersionEndpoints
{
    /// <summary>
    /// Registers all model version management endpoints on the provided <paramref name="routes"/> builder.
    /// Call this from <c>Program.cs</c> after <c>app.UseAuthorization()</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapModelVersionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes
            .MapGroup("/api/admin/ai-gateway")
            .RequireAuthorization(RbacPolicies.AdminOnly)
            .WithTags("AI Gateway — Admin");

        // ── GET /api/admin/ai-gateway/versions ────────────────────────────────
        group.MapGet("/versions", GetVersionsAsync)
             .WithName("GetAiGatewayVersions")
             .WithSummary("Returns the current active and previous model versions for each provider.");

        // ── POST /api/admin/ai-gateway/versions/rollback ─────────────────────
        group.MapPost("/versions/rollback", RollbackVersionAsync)
             .WithName("RollbackAiGatewayVersion")
             .WithSummary("Reverts the specified provider to its previous model version.");

        return routes;
    }

    // ── Handlers ──────────────────────────────────────────────────────────────

    private static IResult GetVersionsAsync(IModelVersionRegistry registry)
    {
        var versions = registry.GetAllVersions();

        var response = versions.Values.Select(e => new
        {
            e.ProviderName,
            e.ActiveModelId,
            e.PreviousModelId,
            e.ActivatedAt,
            e.ActivatedBy,
        });

        return Results.Ok(response);
    }

    private static async Task<IResult> RollbackVersionAsync(
        [FromBody]             RollbackRequest      request,
        IModelVersionRegistry                       registry,
        IAuditLogService                            auditLog,
        ClaimsPrincipal                             user,
        CancellationToken                           ct)
    {
        // Validate request binding (required fields already enforced by data annotations at minimal API level).
        if (string.IsNullOrWhiteSpace(request.Provider))
            return Results.BadRequest("Provider is required.");

        if (string.IsNullOrWhiteSpace(request.Reason))
            return Results.BadRequest("Reason is required.");

        var normalizedProvider = request.Provider.Trim().ToLowerInvariant();

        if (normalizedProvider is not ("openai" or "anthropic"))
            return Results.BadRequest(
                "Invalid provider. Supported values: 'openai', 'anthropic'.");

        // Resolve the admin user ID from JWT claims (sub or nameidentifier).
        var adminUserId = user.FindFirstValue(ClaimTypes.NameIdentifier)
                       ?? user.FindFirstValue("sub")
                       ?? "unknown";

        // ── Execute rollback ─────────────────────────────────────────────────
        var result = registry.ExecuteRollback(normalizedProvider, adminUserId, request.Reason);

        if (!result.Success)
        {
            // HTTP 409 Conflict when no previous version is available.
            return Results.Conflict(new { result.ErrorMessage });
        }

        // ── Persist audit log entry (append-only; no PII) ────────────────────
        // resourceType encodes the rollback context as a structured string.
        // resourceId is null — model version entries are not database entities.
        await auditLog.LogAsync(
            action:       AuditAction.AiModelVersionRollback,
            userId:       Guid.TryParse(adminUserId, out var adminGuid) ? adminGuid : null,
            resourceType: $"AIGateway:ModelVersion:{normalizedProvider}:{result.PreviousVersion}->{result.RolledBackToVersion}",
            ipAddress:    "system",
            userAgent:    "ModelVersionEndpoints",
            resourceId:   null,
            cancellationToken: ct,
            systemEvent:  false);

        return Results.Ok(result);
    }
}
