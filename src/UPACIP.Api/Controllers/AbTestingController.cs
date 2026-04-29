using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPACIP.Api.Authorization;
using UPACIP.Service.AiTesting;
using UPACIP.Service.AiTesting.Models;
using Asp.Versioning;

namespace UPACIP.Api.Controllers;

// ── Request / Response DTOs ───────────────────────────────────────────────────

/// <summary>Body for creating a new A/B experiment.</summary>
public sealed record CreateAbExperimentRequest
{
    /// <summary>Model identifier for the control (current) variant.</summary>
    [Required, MaxLength(200)]
    public string ControlModelId { get; init; } = string.Empty;

    /// <summary>
    /// Model identifier for the candidate (new) variant.
    /// Must differ from <see cref="ControlModelId"/>.
    /// </summary>
    [Required, MaxLength(200)]
    public string CandidateModelId { get; init; } = string.Empty;

    /// <summary>Percentage of requests (1–99) routed to the candidate model.</summary>
    [Required, Range(1, 99)]
    public int TrafficSplitPercentage { get; init; }

    /// <summary>Human-readable description of what is being tested.</summary>
    [MaxLength(1000)]
    public string Description { get; init; } = string.Empty;
}

/// <summary>Summary response for an experiment.</summary>
public sealed record AbExperimentResponse
{
    public Guid     Id                     { get; init; }
    public string   ControlModelId         { get; init; } = string.Empty;
    public string   CandidateModelId       { get; init; } = string.Empty;
    public int      TrafficSplitPercentage  { get; init; }
    public string   Status                 { get; init; } = string.Empty;
    public DateTime StartDate              { get; init; }
    public DateTime? EndDate               { get; init; }
    public string   Description            { get; init; } = string.Empty;
    public string   CreatedByUserId        { get; init; } = string.Empty;
}

// ── Controller ────────────────────────────────────────────────────────────────

/// <summary>
/// Admin-only endpoints for managing A/B experiments between AI model versions
/// (US_080 task_001, AC-1, AC-2, AIR-O10).
///
/// Routes:
///   POST   /api/admin/ab-tests                  — Create and activate a new experiment.
///   GET    /api/admin/ab-tests                  — List all experiments (status filter optional).
///   GET    /api/admin/ab-tests/{id}/results     — Get aggregated metric comparison.
///   POST   /api/admin/ab-tests/{id}/terminate   — Immediately route 100% to control model.
///   POST   /api/admin/ab-tests/{id}/pause       — Pause experiment, retain collected data.
///
/// Authorization (OWASP A01):
///   All endpoints require the Admin role (AdminOnly policy).
///
/// Audit logging (AIR-S04, NFR-012):
///   Lifecycle operations (create, terminate, pause) log admin user ID, experiment ID,
///   and operation at Information level. No PII is logged.
/// </summary>
[ApiVersion("1.0")]
[ApiController]
[Route("api/admin/ab-tests")]
[Authorize(Policy = RbacPolicies.AdminOnly)]
[Produces("application/json")]
public sealed class AbTestingController : ControllerBase
{
    private readonly IAbTestingService              _abService;
    private readonly ILogger<AbTestingController>   _logger;

    public AbTestingController(
        IAbTestingService            abService,
        ILogger<AbTestingController> logger)
    {
        _abService = abService;
        _logger    = logger;
    }

    // ── POST /api/admin/ab-tests ──────────────────────────────────────────────

    /// <summary>
    /// Creates and activates a new A/B experiment.
    /// Any currently active experiment is automatically paused.
    /// </summary>
    /// <response code="201">Experiment created and activated.</response>
    /// <response code="400">Request body is invalid (model IDs identical, split out of range).</response>
    [HttpPost]
    [ProducesResponseType(typeof(AbExperimentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(
        [FromBody] CreateAbExperimentRequest request,
        CancellationToken                   ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        if (request.ControlModelId.Equals(request.CandidateModelId, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "ControlModelId and CandidateModelId must differ." });

        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown-admin";

        var experiment = new AbExperiment
        {
            ControlModelId         = request.ControlModelId,
            CandidateModelId       = request.CandidateModelId,
            TrafficSplitPercentage  = request.TrafficSplitPercentage,
            Description            = request.Description,
            CreatedByUserId        = adminId,
        };

        var created = await _abService.CreateExperimentAsync(experiment, ct);

        _logger.LogInformation(
            "AbTestingAdmin: experiment created. " +
            "AdminId={AdminId} ExperimentId={Id} ControlModel={Control} " +
            "CandidateModel={Candidate} Split={Split}%",
            adminId, created.Id, created.ControlModelId,
            created.CandidateModelId, created.TrafficSplitPercentage);

        return CreatedAtAction(
            nameof(GetResults),
            new { id = created.Id },
            MapToResponse(created));
    }

    // ── GET /api/admin/ab-tests ───────────────────────────────────────────────

    /// <summary>
    /// Lists all A/B experiments, optionally filtered by status.
    /// </summary>
    /// <param name="status">Optional filter: Active, Paused, Terminated, Completed.</param>
    /// <param name="page">1-based page number (default 1).</param>
    /// <param name="pageSize">Page size 1–100 (default 20).</param>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<AbExperimentResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? status   = null,
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 20,
        CancellationToken   ct       = default)
    {
        var experiments = await _abService.ListExperimentsAsync(status, page, pageSize, ct);
        return Ok(experiments.Select(MapToResponse));
    }

    // ── GET /api/admin/ab-tests/{id}/results ─────────────────────────────────

    /// <summary>
    /// Returns aggregated metric comparison (accuracy, latency, cost) for both variants.
    /// </summary>
    /// <param name="id">Experiment identifier.</param>
    [HttpGet("{id:guid}/results")]
    [ProducesResponseType(typeof(AbExperimentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetResults(
        [FromRoute] Guid  id,
        CancellationToken ct = default)
    {
        var result = await _abService.GetExperimentResultsAsync(id, ct);
        return Ok(result);
    }

    // ── POST /api/admin/ab-tests/{id}/terminate ───────────────────────────────

    /// <summary>
    /// Immediately terminates the experiment and routes 100% of traffic to the control model.
    /// Use when A/B results show the candidate model is significantly worse (edge case).
    /// </summary>
    /// <param name="id">Experiment to terminate.</param>
    /// <response code="200">Experiment terminated; traffic reverted to control model.</response>
    [HttpPost("{id:guid}/terminate")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Terminate(
        [FromRoute] Guid  id,
        CancellationToken ct = default)
    {
        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown-admin";

        await _abService.TerminateExperimentAsync(id, ct);

        _logger.LogInformation(
            "AbTestingAdmin: experiment terminated. AdminId={AdminId} ExperimentId={Id}",
            adminId, id);

        return Ok(new
        {
            experimentId = id,
            status       = "Terminated",
            message      = "All traffic reverted to control model immediately.",
        });
    }

    // ── POST /api/admin/ab-tests/{id}/pause ──────────────────────────────────

    /// <summary>
    /// Pauses the experiment, routing all traffic to the control model without discarding
    /// collected metrics data.
    /// </summary>
    /// <param name="id">Experiment to pause.</param>
    [HttpPost("{id:guid}/pause")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Pause(
        [FromRoute] Guid  id,
        CancellationToken ct = default)
    {
        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown-admin";

        await _abService.PauseExperimentAsync(id, ct);

        _logger.LogInformation(
            "AbTestingAdmin: experiment paused. AdminId={AdminId} ExperimentId={Id}",
            adminId, id);

        return Ok(new
        {
            experimentId = id,
            status       = "Paused",
            message      = "Traffic reverted to control model. Collected metrics are retained.",
        });
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static AbExperimentResponse MapToResponse(AbExperiment e) => new()
    {
        Id                    = e.Id,
        ControlModelId        = e.ControlModelId,
        CandidateModelId      = e.CandidateModelId,
        TrafficSplitPercentage = e.TrafficSplitPercentage,
        Status                = e.Status.ToString(),
        StartDate             = e.StartDate,
        EndDate               = e.EndDate,
        Description           = e.Description,
        CreatedByUserId       = e.CreatedByUserId,
    };
}
