using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.Api.Authorization;
using UPACIP.DataAccess;
using UPACIP.Service.Monitoring;
using UPACIP.Service.Monitoring.Models;

namespace UPACIP.Api.Controllers;

// ── Request DTOs ──────────────────────────────────────────────────────────────

/// <summary>Body for a manual dependency health override.</summary>
public sealed record SystemStatusOverrideRequest
{
    /// <summary>The dependency category to override.</summary>
    [Required]
    public DependencyCategory Category { get; init; }

    /// <summary><c>true</c> marks the dependency as healthy; <c>false</c> activates degradation.</summary>
    [Required]
    public bool IsHealthy { get; init; }
}

// ── Controller ────────────────────────────────────────────────────────────────

/// <summary>
/// Admin endpoints for querying and manually overriding the system degradation state
/// (US_083 task_002, AC-1, AC-2, AC-3).
///
/// Routes:
///   GET    /api/admin/system-status            — Current degradation state (StaffOrAdmin).
///   POST   /api/admin/system-status/override   — Manual dependency override (AdminOnly).
///   GET    /api/admin/system-status/history    — Outage records from last 24 hours (AdminOnly).
///
/// Authorization (OWASP A01):
///   GET  → StaffOrAdmin (staff need to see degradation status for patient-facing decisions).
///   POST → AdminOnly (manual overrides are high-impact operations).
///   GET /history → AdminOnly (audit data).
///
/// Audit logging (AIR-S04, NFR-012):
///   Override actions log admin user ID, target category, and new health value at
///   Information level.  No PII is logged.
/// </summary>
[ApiController]
[Route("api/admin/system-status")]
[Produces("application/json")]
public sealed class SystemStatusController : ControllerBase
{
    private readonly IDegradationModeManager         _degradationManager;
    private readonly IServiceScopeFactory            _scopeFactory;
    private readonly ILogger<SystemStatusController> _logger;

    public SystemStatusController(
        IDegradationModeManager degradationManager,
        IServiceScopeFactory scopeFactory,
        ILogger<SystemStatusController> logger)
    {
        _degradationManager = degradationManager;
        _scopeFactory       = scopeFactory;
        _logger             = logger;
    }

    // ── GET /api/admin/system-status ─────────────────────────────────────────

    /// <summary>Returns the current system degradation state.</summary>
    /// <response code="200">Current <see cref="DegradationState"/> snapshot.</response>
    [HttpGet]
    [Authorize(Policy = RbacPolicies.StaffOrAdmin)]
    [ProducesResponseType(typeof(DegradationState), StatusCodes.Status200OK)]
    public IActionResult GetCurrentStatus()
    {
        var state = _degradationManager.GetCurrentState();
        return Ok(state);
    }

    // ── POST /api/admin/system-status/override ───────────────────────────────

    /// <summary>Manually overrides the health status of a dependency category.</summary>
    /// <remarks>
    /// Setting <c>isHealthy=false</c> activates degradation for the category; setting
    /// <c>isHealthy=true</c> recovers it.  The change takes effect immediately on the
    /// current instance (Redis flag update is fire-and-forget for other instances).
    /// </remarks>
    /// <response code="200">Override applied successfully.</response>
    /// <response code="400">Request body is invalid.</response>
    [HttpPost("override")]
    [Authorize(Policy = RbacPolicies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public IActionResult Override([FromBody] SystemStatusOverrideRequest request)
    {
        var adminId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                      ?? "unknown";

        if (request.IsHealthy)
        {
            _degradationManager.DeactivateDegradation(request.Category);
            _logger.LogInformation(
                "ADMIN_OVERRIDE: Category={Category} marked healthy by Admin={AdminId}",
                request.Category, adminId);
        }
        else
        {
            _degradationManager.ActivateDegradation(request.Category);
            _logger.LogInformation(
                "ADMIN_OVERRIDE: Category={Category} marked unhealthy by Admin={AdminId}",
                request.Category, adminId);
        }

        return Ok(new { applied = true, category = request.Category.ToString(), isHealthy = request.IsHealthy });
    }

    // ── GET /api/admin/system-status/history ─────────────────────────────────

    /// <summary>Returns outage records started in the last 24 hours.</summary>
    /// <response code="200">List of recent outage records.</response>
    [HttpGet("history")]
    [Authorize(Policy = RbacPolicies.AdminOnly)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetHistory(CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var records = await db.OutageRecords
            .Where(r => r.StartedAt >= cutoff)
            .OrderByDescending(r => r.StartedAt)
            .Select(r => new
            {
                r.Id,
                r.StartedAt,
                r.ResolvedAt,
                r.AffectedServices,
                r.ImpactLevel,
                r.AlertSentAt,
            })
            .ToListAsync(ct);

        return Ok(records);
    }
}
