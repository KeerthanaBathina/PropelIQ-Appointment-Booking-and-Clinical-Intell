using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPACIP.Api.Authorization;
using UPACIP.DataAccess.Entities;
using UPACIP.Service.Backup;
using UPACIP.Service.Backup.Models;
using Asp.Versioning;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Admin-only endpoints for backup restoration testing, quarterly compliance tracking,
/// and point-in-time recovery (US_089 task_003, US_090 task_002, AC-2, AC-3, AC-4, DR-026, DR-027).
///
/// Routes:
///   POST /api/admin/backup/restore-test             — Trigger quarterly restoration test.
///   GET  /api/admin/backup/restore-test/status      — Quarterly test scheduling status.
///   POST /api/admin/backup/pitr                     — Execute point-in-time recovery.
///   GET  /api/admin/backup/pitr/feasibility         — Pre-flight feasibility check (dry run).
///   GET  /api/admin/backup/pitr/history             — Paginated PITR audit history.
///
/// Authorization (OWASP A01, NFR-011): All endpoints require the Admin role.
/// </summary>
[ApiVersion("1.0")]
[ApiController]
[Authorize(Policy = RbacPolicies.AdminOnly)]
[Route("api/admin/backup")]
[Produces("application/json")]
public sealed class BackupController : ControllerBase
{
    private readonly IBackupRestorationTestService  _restorationTestService;
    private readonly IPointInTimeRecoveryService    _pitrService;
    private readonly ILogger<BackupController>      _logger;

    public BackupController(
        IBackupRestorationTestService restorationTestService,
        IPointInTimeRecoveryService   pitrService,
        ILogger<BackupController>     logger)
    {
        _restorationTestService = restorationTestService;
        _pitrService            = pitrService;
        _logger                 = logger;
    }

    // ── Restoration testing ──────────────────────────────────────────────────

    /// <summary>
    /// Triggers a quarterly backup restoration test (AC-3, AC-4, DR-026).
    /// </summary>
    [HttpPost("restore-test")]
    [ProducesResponseType(typeof(RestorationTestResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RunRestorationTest(CancellationToken ct)
    {
        var performedBy = ResolveAdminIdentity();

        _logger.LogInformation(
            "BackupController: restoration test initiated by admin {Admin}.", performedBy);

        try
        {
            var result = await _restorationTestService.RunRestorationTestAsync(performedBy, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "BackupController: unhandled exception during restoration test. Admin={Admin}.",
                performedBy);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An unexpected error occurred during the restoration test." });
        }
    }

    /// <summary>
    /// Returns the quarterly backup restoration test scheduling status (AC-3).
    /// </summary>
    [HttpGet("restore-test/status")]
    [ProducesResponseType(typeof(QuarterlyTestStatus), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetRestorationTestStatus(CancellationToken ct)
    {
        var status = await _restorationTestService.GetQuarterlyTestStatusAsync(ct);
        return Ok(status);
    }

    // ── Point-in-time recovery ───────────────────────────────────────────────

    /// <summary>
    /// Executes a full point-in-time recovery to the specified UTC timestamp (AC-2, AC-4, DR-027).
    ///
    /// This is a long-running operation (up to <c>MaxRecoveryTimeoutMinutes</c> — default 4 hours).
    /// The response is returned synchronously once the pipeline completes.
    /// Set <c>dryRun=true</c> in the request body to perform pre-flight validation only.
    /// </summary>
    /// <response code="200">Recovery pipeline completed — inspect <c>success</c> for pass/fail.</response>
    /// <response code="400">Request validation failed (invalid timestamp, missing fields).</response>
    /// <response code="401">Unauthenticated.</response>
    /// <response code="403">Authenticated but not an Admin.</response>
    /// <response code="500">Unhandled server error during recovery.</response>
    [HttpPost("pitr")]
    [ProducesResponseType(typeof(PitrResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> ExecutePointInTimeRecovery(
        [FromBody] PitrRequestBody body,
        CancellationToken          ct)
    {
        if (body.TargetTimestampUtc >= DateTime.UtcNow)
            return BadRequest(new { error = "TargetTimestampUtc must be in the past." });

        var performedBy = ResolveAdminIdentity();

        var request = new PitrRequest
        {
            TargetTimestampUtc = body.TargetTimestampUtc,
            PerformedBy        = performedBy,
            DryRun             = body.DryRun,
            ValidateIntegrity  = body.ValidateIntegrity,
        };

        _logger.LogInformation(
            "BackupController: PITR initiated by admin {Admin}. Target={Target}, DryRun={DryRun}.",
            performedBy, body.TargetTimestampUtc, body.DryRun);

        try
        {
            var result = await _pitrService.ExecuteRecoveryAsync(request, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "BackupController: unhandled exception during PITR. Admin={Admin}.", performedBy);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new { error = "An unexpected error occurred during point-in-time recovery." });
        }
    }

    /// <summary>
    /// Returns a pre-flight feasibility assessment for a target recovery timestamp
    /// without executing the actual recovery (AC-2).
    /// </summary>
    /// <param name="targetTimestamp">ISO 8601 UTC timestamp to assess feasibility for.</param>
    /// <response code="200">Feasibility assessment returned.</response>
    /// <response code="400">Invalid or future timestamp.</response>
    [HttpGet("pitr/feasibility")]
    [ProducesResponseType(typeof(PitrResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPitrFeasibility(
        [FromQuery] DateTime targetTimestamp,
        CancellationToken    ct)
    {
        if (targetTimestamp >= DateTime.UtcNow)
            return BadRequest(new { error = "targetTimestamp must be in the past." });

        var request = new PitrRequest
        {
            TargetTimestampUtc = targetTimestamp,
            PerformedBy        = ResolveAdminIdentity(),
            DryRun             = true,
            ValidateIntegrity  = false,
        };

        var result = await _pitrService.ExecuteRecoveryAsync(request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Returns paginated PITR audit history (most recent first).
    /// </summary>
    /// <param name="page">1-based page number. Default: 1.</param>
    /// <param name="pageSize">Records per page. Default: 20.</param>
    [HttpGet("pitr/history")]
    [ProducesResponseType(typeof(List<RecoveryLog>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPitrHistory(
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct     = default)
    {
        if (page < 1)     page     = 1;
        if (pageSize < 1) pageSize = 1;
        if (pageSize > 100) pageSize = 100;   // cap to prevent oversized queries

        var history = await _pitrService.GetRecoveryHistoryAsync(page, pageSize, ct);
        return Ok(history);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private string ResolveAdminIdentity() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue(ClaimTypes.Name)
        ?? "unknown";
}

/// <summary>
/// Request body for the POST /api/admin/backup/pitr endpoint.
/// </summary>
public sealed record PitrRequestBody
{
    /// <summary>The exact UTC timestamp to recover the database to. Must be in the past.</summary>
    public required DateTime TargetTimestampUtc { get; init; }

    /// <summary>When true, only pre-flight validation is performed — no recovery executes.</summary>
    public bool DryRun { get; init; } = false;

    /// <summary>Whether to run post-recovery row count and checksum validation (AC-3).</summary>
    public bool ValidateIntegrity { get; init; } = true;
}

