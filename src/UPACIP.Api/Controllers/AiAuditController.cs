using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UPACIP.Api.Authorization;
using UPACIP.DataAccess;
using UPACIP.Service.AiAudit;
using UPACIP.Service.AiAudit.Models;
using Asp.Versioning;

namespace UPACIP.Api.Controllers;

// ── Response DTOs ─────────────────────────────────────────────────────────────

/// <summary>Single AI audit log entry response (AC-3).</summary>
public sealed record AiAuditLogResponse
{
    public Guid     Id              { get; init; }
    public string   Prompt          { get; init; } = string.Empty;
    public string   Response        { get; init; } = string.Empty;
    public string   ModelVersion    { get; init; } = string.Empty;
    public int      InputTokens     { get; init; }
    public int      OutputTokens    { get; init; }
    public int      TotalTokens     { get; init; }
    public long     LatencyMs       { get; init; }
    public float?   ConfidenceScore { get; init; }
    public string   RequestType     { get; init; } = string.Empty;
    public Guid?    PatientId       { get; init; }
    public Guid?    AbExperimentId  { get; init; }
    public string?  AbVariant       { get; init; }
    public string   UserId          { get; init; } = string.Empty;
    public DateTime CreatedAt       { get; init; }
}

/// <summary>Paginated list of audit log entries (AC-4).</summary>
public sealed record AiAuditPageResponse
{
    public IReadOnlyList<AiAuditLogResponse> Items      { get; init; } = [];
    public string?                           NextCursor { get; init; }
    public long                              TotalCount { get; init; }
    public bool                              HasMore    { get; init; }
}

/// <summary>Per-model aggregated stats for the summary endpoint.</summary>
public sealed record ModelVersionStats
{
    public string  ModelVersion     { get; init; } = string.Empty;
    public long    TotalRequests    { get; init; }
    public double  AvgLatencyMs     { get; init; }
    public long    TotalInputTokens  { get; init; }
    public long    TotalOutputTokens { get; init; }
}

/// <summary>Per-request-type aggregated stats for the summary endpoint.</summary>
public sealed record RequestTypeStats
{
    public string RequestType      { get; init; } = string.Empty;
    public long   TotalRequests    { get; init; }
    public double AvgLatencyMs     { get; init; }
    public double AvgConfidence    { get; init; }
}

/// <summary>Aggregated AI audit summary (AC-4).</summary>
public sealed record AiAuditSummaryResponse
{
    public IReadOnlyList<ModelVersionStats>  ByModelVersion  { get; init; } = [];
    public IReadOnlyList<RequestTypeStats>   ByRequestType   { get; init; } = [];
    public DateTime                          DateFrom        { get; init; }
    public DateTime                          DateTo          { get; init; }
}

// ── Controller ────────────────────────────────────────────────────────────────

/// <summary>
/// Admin-only AI audit log query API (US_080 task_002, AC-3, AC-4, AIR-S04).
///
/// <para>Routes (all require Admin role):</para>
/// <list type="bullet">
///   <item><c>GET  /api/admin/ai-audit</c>         — Paginated query with filters.</item>
///   <item><c>GET  /api/admin/ai-audit/{id}</c>    — Single entry by ID.</item>
///   <item><c>GET  /api/admin/ai-audit/summary</c> — Aggregated stats (requires date range).</item>
/// </list>
///
/// <para>
/// Authorization (OWASP A01): All endpoints require <c>AdminOnly</c> policy.
/// Patient data is accessible only to authenticated administrators; no patient-facing
/// or staff-facing routes are provided.
/// </para>
///
/// <para>
/// PII policy (AIR-S01): The <c>prompt</c> field in responses contains post-PII-redacted
/// text only.  Raw patient identifiers MUST NOT appear in audit records.
/// </para>
/// </summary>
[ApiVersion("1.0")]
[ApiController]
[Route("api/admin/ai-audit")]
[Authorize(Policy = RbacPolicies.AdminOnly)]
[Produces("application/json")]
public sealed class AiAuditController : ControllerBase
{
    private readonly IAiAuditService            _auditService;
    private readonly IServiceScopeFactory       _scopeFactory;
    private readonly ILogger<AiAuditController> _logger;

    public AiAuditController(
        IAiAuditService            auditService,
        IServiceScopeFactory       scopeFactory,
        ILogger<AiAuditController> logger)
    {
        _auditService = auditService;
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    // ── GET /api/admin/ai-audit ───────────────────────────────────────────────

    /// <summary>
    /// Returns a filtered, paginated list of AI audit log entries (AC-4).
    /// </summary>
    /// <param name="dateFrom">Inclusive start date (ISO 8601 UTC). Optional.</param>
    /// <param name="dateTo">Inclusive end date (ISO 8601 UTC). Optional.</param>
    /// <param name="modelVersion">Exact model version filter. Optional.</param>
    /// <param name="requestType">Exact request type filter. Optional.</param>
    /// <param name="confidenceMin">Minimum confidence score [0,1]. Optional.</param>
    /// <param name="confidenceMax">Maximum confidence score [0,1]. Optional.</param>
    /// <param name="cursor">Cursor from previous page. Null = first page.</param>
    /// <param name="pageSize">Page size (1–200, default 50).</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType(typeof(AiAuditPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> QueryLogs(
        [FromQuery] DateTime? dateFrom       = null,
        [FromQuery] DateTime? dateTo         = null,
        [FromQuery] string?   modelVersion   = null,
        [FromQuery] string?   requestType    = null,
        [FromQuery] float?    confidenceMin  = null,
        [FromQuery] float?    confidenceMax  = null,
        [FromQuery] string?   cursor         = null,
        [FromQuery] int       pageSize       = 50,
        CancellationToken     ct             = default)
    {
        // ── Validate ──────────────────────────────────────────────────────────
        if (dateFrom.HasValue && dateTo.HasValue && dateFrom > dateTo)
            return BadRequest(new { error = "dateFrom must be <= dateTo." });

        if (confidenceMin.HasValue && confidenceMax.HasValue && confidenceMin > confidenceMax)
            return BadRequest(new { error = "confidenceMin must be <= confidenceMax." });

        if (confidenceMin is < 0f or > 1f || confidenceMax is < 0f or > 1f)
            return BadRequest(new { error = "Confidence score values must be in [0, 1]." });

        var filter = new AiAuditQueryFilter
        {
            DateFrom      = dateFrom?.ToUniversalTime(),
            DateTo        = dateTo?.ToUniversalTime(),
            ModelVersion  = modelVersion,
            RequestType   = requestType,
            ConfidenceMin = confidenceMin,
            ConfidenceMax = confidenceMax,
            Cursor        = cursor,
            PageSize      = pageSize,
        };

        try
        {
            var result = await _auditService.QueryAuditLogsAsync(filter, ct);

            return Ok(new AiAuditPageResponse
            {
                Items      = result.Items.Select(MapToResponse).ToList(),
                NextCursor = result.NextCursor,
                TotalCount = result.TotalCount,
                HasMore    = result.HasMore,
            });
        }
        catch (ArgumentException ex) when (ex.ParamName == "cursor")
        {
            return BadRequest(new { error = "Invalid pagination cursor." });
        }
    }

    // ── GET /api/admin/ai-audit/{id} ─────────────────────────────────────────

    /// <summary>
    /// Returns a single AI audit log entry by its unique identifier (AC-3).
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AiAuditLogResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid              id,
        CancellationToken ct = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var entity = await db.AiAuditLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct);

        if (entity is null)
            return NotFound(new { error = $"Audit log entry {id} not found." });

        return Ok(new AiAuditLogResponse
        {
            Id              = entity.Id,
            Prompt          = entity.Prompt,
            Response        = entity.Response,
            ModelVersion    = entity.ModelVersion,
            InputTokens     = entity.InputTokens,
            OutputTokens    = entity.OutputTokens,
            TotalTokens     = entity.TotalTokens,
            LatencyMs       = entity.LatencyMs,
            ConfidenceScore = entity.ConfidenceScore,
            RequestType     = entity.RequestType,
            PatientId       = entity.PatientId,
            AbExperimentId  = entity.AbExperimentId,
            AbVariant       = entity.AbVariant,
            UserId          = entity.UserId,
            CreatedAt       = entity.CreatedAt,
        });
    }

    // ── GET /api/admin/ai-audit/summary ──────────────────────────────────────

    /// <summary>
    /// Returns aggregated AI audit metrics grouped by model version and request type.
    /// A date range is required to scope the aggregation (AC-4).
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(AiAuditSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetSummary(
        [FromQuery, Required] DateTime dateFrom,
        [FromQuery, Required] DateTime dateTo,
        CancellationToken              ct = default)
    {
        if (dateFrom > dateTo)
            return BadRequest(new { error = "dateFrom must be <= dateTo." });

        var from = dateFrom.ToUniversalTime();
        var to   = dateTo.ToUniversalTime();

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var baseQuery = db.AiAuditLogs
            .AsNoTracking()
            .Where(e => e.CreatedAt >= from && e.CreatedAt <= to);

        // ── Aggregate by model version ────────────────────────────────────────
        var byModel = await baseQuery
            .GroupBy(e => e.ModelVersion)
            .Select(g => new ModelVersionStats
            {
                ModelVersion      = g.Key,
                TotalRequests     = g.LongCount(),
                AvgLatencyMs      = g.Average(e => (double)e.LatencyMs),
                TotalInputTokens  = g.Sum(e => (long)e.InputTokens),
                TotalOutputTokens = g.Sum(e => (long)e.OutputTokens),
            })
            .OrderByDescending(s => s.TotalRequests)
            .ToListAsync(ct);

        // ── Aggregate by request type ─────────────────────────────────────────
        var byRequestType = await baseQuery
            .GroupBy(e => e.RequestType)
            .Select(g => new RequestTypeStats
            {
                RequestType   = g.Key,
                TotalRequests = g.LongCount(),
                AvgLatencyMs  = g.Average(e => (double)e.LatencyMs),
                AvgConfidence = g.Where(e => e.ConfidenceScore.HasValue)
                                 .Average(e => (double?)e.ConfidenceScore) ?? 0.0,
            })
            .OrderByDescending(s => s.TotalRequests)
            .ToListAsync(ct);

        return Ok(new AiAuditSummaryResponse
        {
            ByModelVersion = byModel,
            ByRequestType  = byRequestType,
            DateFrom       = from,
            DateTo         = to,
        });
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static AiAuditLogResponse MapToResponse(AiAuditLogEntry e) => new()
    {
        Id              = e.Id,
        Prompt          = e.Prompt,
        Response        = e.Response,
        ModelVersion    = e.ModelVersion,
        InputTokens     = e.InputTokens,
        OutputTokens    = e.OutputTokens,
        TotalTokens     = e.TotalTokens,
        LatencyMs       = e.LatencyMs,
        ConfidenceScore = e.ConfidenceScore,
        RequestType     = e.RequestType,
        PatientId       = e.PatientId,
        AbExperimentId  = e.AbExperimentId,
        AbVariant       = e.AbVariant,
        UserId          = e.UserId,
        CreatedAt       = e.CreatedAt,
    };
}
