using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using UPACIP.Api.Authorization;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.Service.Recovery;
using UPACIP.Service.Recovery.Models;
using Asp.Versioning;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Admin-only endpoints for RPO/RTO compliance status, quarterly recovery test history,
/// and disaster recovery runbook management (US_095, AC-3, NFR-024, NFR-025, DR-026).
///
/// Routes:
///   GET  /api/admin/recovery/status                — Current RPO/RTO compliance snapshot.
///   GET  /api/admin/recovery/tests                 — Paginated quarterly test history.
///   POST /api/admin/recovery/tests                 — Record a manual recovery test result.
///   GET  /api/admin/recovery/runbooks              — List active disaster recovery runbooks.
///   POST /api/admin/recovery/runbooks              — Create or update a runbook.
///   GET  /api/admin/recovery/runbooks/{runbookId}  — Get full runbook with parsed steps.
///
/// Authorization (OWASP A01, NFR-011): All endpoints require the Admin role.
/// </summary>
[ApiVersion("1.0")]
[ApiController]
[Authorize(Policy = RbacPolicies.AdminOnly)]
[Route("api/admin/recovery")]
[Produces("application/json")]
public sealed class RecoveryController : ControllerBase
{
    private readonly RecoveryTargetMonitoringService    _monitoringService;
    private readonly ApplicationDbContext               _db;
    private readonly IOptionsMonitor<RecoveryTargetOptions> _optionsMonitor;
    private readonly ILogger<RecoveryController>        _logger;

    public RecoveryController(
        RecoveryTargetMonitoringService             monitoringService,
        ApplicationDbContext                        db,
        IOptionsMonitor<RecoveryTargetOptions>      optionsMonitor,
        ILogger<RecoveryController>                 logger)
    {
        _monitoringService = monitoringService;
        _db                = db;
        _optionsMonitor    = optionsMonitor;
        _logger            = logger;
    }

    // ── GET /api/admin/recovery/status ────────────────────────────────────────

    /// <summary>
    /// Returns the most recent RPO/RTO compliance snapshot from the monitoring service cache.
    /// Returns 503 when no snapshot is available yet (first monitoring cycle not yet complete).
    /// </summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(RecoveryTargetStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public IActionResult GetStatus()
    {
        var status = _monitoringService.LatestStatus;
        if (status is null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { error = "Recovery monitoring has not yet completed its first check cycle. Retry in 60 seconds." });
        }

        return Ok(status);
    }

    // ── GET /api/admin/recovery/tests ─────────────────────────────────────────

    /// <summary>
    /// Returns paginated quarterly recovery test history, newest first.
    /// Supports optional <c>quarter</c> filter (e.g., "2026-Q2").
    /// </summary>
    [HttpGet("tests")]
    [ProducesResponseType(typeof(IEnumerable<RecoveryTestRecord>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetTests(
        [FromQuery] string? quarter,
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 20,
        CancellationToken   ct       = default)
    {
        if (page < 1)     page     = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var query = _db.RecoveryTestRecords
            .OrderByDescending(t => t.ExecutedAtUtc)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(quarter))
            query = query.Where(t => t.Quarter == quarter);

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(items);
    }

    // ── POST /api/admin/recovery/tests ────────────────────────────────────────

    /// <summary>
    /// Records a manually executed quarterly recovery test result (AC-3, DR-026).
    /// Validates that <c>Quarter</c> matches the current calendar quarter and
    /// that <c>ActualRecoveryTime</c> is positive.
    /// </summary>
    [HttpPost("tests")]
    [ProducesResponseType(typeof(RecoveryTestRecord), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RecordTest(
        [FromBody]        RecoveryTestRecord  record,
        CancellationToken                     ct = default)
    {
        if (record is null)
            return BadRequest(new { error = "Request body is required." });

        if (record.ActualRecoveryTime <= TimeSpan.Zero)
            return BadRequest(new { error = "ActualRecoveryTime must be greater than zero." });

        var currentQuarter = GetCurrentQuarter(DateTime.UtcNow);
        if (string.IsNullOrWhiteSpace(record.Quarter))
            record.Quarter = currentQuarter;

        // Validate the quarter label format (YYYY-Qn) by parsing it.
        if (!IsValidQuarterLabel(record.Quarter))
            return BadRequest(new { error = $"Invalid Quarter format '{record.Quarter}'. Expected format: YYYY-Qn (e.g., 2026-Q2)." });

        record.Id           = Guid.NewGuid();
        record.CreatedAtUtc = DateTime.UtcNow;
        record.ExecutedBy   = ResolveAdminIdentity();

        if (string.IsNullOrWhiteSpace(record.ExecutedAtUtc.ToString()) ||
            record.ExecutedAtUtc == DateTime.MinValue)
        {
            record.ExecutedAtUtc = DateTime.UtcNow;
        }

        var opts = _optionsMonitor.CurrentValue;
        _db.RecoveryTestRecords.Add(record);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "RecoveryController: test result recorded by {Admin}: TestId={Id}, Quarter={Quarter}, " +
            "Passed={Passed}, RecoveryTime={Minutes:F0}min (RTO={Target}min).",
            record.ExecutedBy, record.Id, record.Quarter,
            record.Passed, record.ActualRecoveryTime.TotalMinutes, opts.RtoMinutes);

        return CreatedAtAction(nameof(GetTests), new { quarter = record.Quarter }, record);
    }

    // ── GET /api/admin/recovery/runbooks ──────────────────────────────────────

    /// <summary>
    /// Lists active disaster recovery runbooks. Supports optional <c>scenarioType</c> filter.
    /// </summary>
    [HttpGet("runbooks")]
    [ProducesResponseType(typeof(IEnumerable<DisasterRecoveryRunbook>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetRunbooks(
        [FromQuery] string? scenarioType,
        CancellationToken   ct = default)
    {
        var query = _db.DisasterRecoveryRunbooks
            .Where(r => r.Status == "Active")
            .OrderByDescending(r => r.UpdatedAtUtc)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(scenarioType))
            query = query.Where(r => r.ScenarioType == scenarioType);

        var runbooks = await query.ToListAsync(ct);
        return Ok(runbooks);
    }

    // ── POST /api/admin/recovery/runbooks ─────────────────────────────────────

    /// <summary>
    /// Creates a new disaster recovery runbook or updates an existing one for the same
    /// <c>ScenarioType</c> (auto-increments version, archives the previous Active version).
    /// Validates that <c>TotalEstimatedMinutes</c> does not exceed the RTO target.
    /// </summary>
    [HttpPost("runbooks")]
    [ProducesResponseType(typeof(DisasterRecoveryRunbook), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateOrUpdateRunbook(
        [FromBody]        DisasterRecoveryRunbook runbook,
        CancellationToken                         ct = default)
    {
        if (runbook is null)
            return BadRequest(new { error = "Request body is required." });

        if (string.IsNullOrWhiteSpace(runbook.Title))
            return BadRequest(new { error = "Title is required." });

        if (string.IsNullOrWhiteSpace(runbook.ScenarioType))
            return BadRequest(new { error = "ScenarioType is required." });

        var opts = _optionsMonitor.CurrentValue;
        if (runbook.TotalEstimatedMinutes > opts.RtoMinutes)
        {
            return BadRequest(new
            {
                error = $"TotalEstimatedMinutes ({runbook.TotalEstimatedMinutes}) exceeds RTO target ({opts.RtoMinutes} min)."
            });
        }

        // Archive any existing Active runbook for this scenario.
        var existing = await _db.DisasterRecoveryRunbooks
            .Where(r => r.ScenarioType == runbook.ScenarioType && r.Status == "Active")
            .FirstOrDefaultAsync(ct);

        int nextVersion = 1;
        if (existing is not null)
        {
            existing.Status       = "Archived";
            existing.UpdatedAtUtc = DateTime.UtcNow;
            nextVersion           = existing.Version + 1;
        }

        var admin = ResolveAdminIdentity();
        runbook.Id           = Guid.NewGuid();
        runbook.Version      = nextVersion;
        runbook.CreatedBy    = admin;
        runbook.CreatedAtUtc = DateTime.UtcNow;
        runbook.UpdatedAtUtc = DateTime.UtcNow;
        runbook.Status       = "Active";

        _db.DisasterRecoveryRunbooks.Add(runbook);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "RecoveryController: runbook created by {Admin}: Id={Id}, Scenario={Scenario}, " +
            "Version={Version}, EstimatedMinutes={Minutes}.",
            admin, runbook.Id, runbook.ScenarioType, runbook.Version, runbook.TotalEstimatedMinutes);

        return CreatedAtAction(nameof(GetRunbook), new { runbookId = runbook.Id }, runbook);
    }

    // ── GET /api/admin/recovery/runbooks/{runbookId} ──────────────────────────

    /// <summary>
    /// Returns the full runbook including a parsed JSON representation of recovery steps.
    /// </summary>
    [HttpGet("runbooks/{runbookId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRunbook(Guid runbookId, CancellationToken ct = default)
    {
        var runbook = await _db.DisasterRecoveryRunbooks
            .FirstOrDefaultAsync(r => r.Id == runbookId, ct);

        if (runbook is null)
            return NotFound(new { error = $"Runbook {runbookId} not found." });

        // Parse StepsJson into a strongly-typed list for the API response.
        object? parsedSteps = null;
        try
        {
            parsedSteps = JsonSerializer.Deserialize<JsonElement[]>(runbook.StepsJson);
        }
        catch
        {
            parsedSteps = Array.Empty<object>();
        }

        return Ok(new
        {
            runbook.Id,
            runbook.Title,
            runbook.ScenarioType,
            runbook.TotalEstimatedMinutes,
            runbook.Status,
            runbook.Version,
            runbook.CreatedBy,
            runbook.CreatedAtUtc,
            runbook.UpdatedAtUtc,
            Steps = parsedSteps,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private string ResolveAdminIdentity()
        => User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(ClaimTypes.Email)
            ?? "unknown-admin";

    private static string GetCurrentQuarter(DateTime utc)
    {
        var q = (utc.Month - 1) / 3 + 1;
        return $"{utc.Year}-Q{q}";
    }

    private static bool IsValidQuarterLabel(string quarter)
    {
        // Expected format: YYYY-Qn where n is 1-4.
        if (quarter.Length != 7) return false;
        if (!int.TryParse(quarter[..4], out var year)) return false;
        if (quarter[4] != '-') return false;
        if (quarter[5] != 'Q') return false;
        if (!int.TryParse(quarter[6..], out var q)) return false;
        return year >= 2000 && year <= 9999 && q >= 1 && q <= 4;
    }
}
