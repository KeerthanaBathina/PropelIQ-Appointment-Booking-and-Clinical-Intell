using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPACIP.Api.Authorization;
using UPACIP.Api.Middleware;
using UPACIP.Api.Models;
using UPACIP.Service.Rag.Refresh;
using UPACIP.Service.Rag.Refresh.Models;
using UPACIP.Service.VectorSearch;
using Asp.Versioning;

namespace UPACIP.Api.Controllers;

// ── Request / Response DTOs ───────────────────────────────────────────────────

/// <summary>A single code entry in the refresh payload.</summary>
public sealed record KbCodeEntryDto
{
    /// <example>E11.65</example>
    [Required, MaxLength(20)]
    public string CodeValue { get; init; } = string.Empty;

    /// <example>ICD-10</example>
    [Required]
    public string CodeSystem { get; init; } = string.Empty;

    /// <example>Type 2 diabetes mellitus with hyperglycemia</example>
    [Required, MaxLength(4000)]
    public string Description { get; init; } = string.Empty;

    /// <summary>Set to <c>true</c> to explicitly retire this code.</summary>
    public bool IsDeprecated { get; init; }
}

/// <summary>Admin payload for triggering a knowledge-base refresh.</summary>
public sealed record KbRefreshRequestDto
{
    /// <summary>Non-empty list of code entries for this library version.</summary>
    [Required, MinLength(1)]
    public IReadOnlyList<KbCodeEntryDto> Entries { get; init; } = [];

    /// <summary>
    /// Target embedding category.
    /// 0 = MedicalTerminology, 1 = IntakeTemplate, 2 = CodingGuideline.
    /// </summary>
    [Required]
    public EmbeddingCategory TargetCategory { get; init; }

    /// <example>ICD-10-CM-2026-Q2</example>
    [Required, MaxLength(100)]
    public string SourceVersion { get; init; } = string.Empty;
}

/// <summary>Refresh result returned to the admin caller.</summary>
public sealed record KbRefreshResultDto
{
    public int    NewCodesAdded   { get; init; }
    public int    CodesUpdated    { get; init; }
    public int    CodesDeprecated { get; init; }
    public int    TotalProcessed  { get; init; }
    public string Duration        { get; init; } = string.Empty;
    public string Status          { get; init; } = string.Empty;
    public string? ErrorMessage   { get; init; }
}

// ── Controller ────────────────────────────────────────────────────────────────

/// <summary>
/// Admin-only endpoints for triggering and monitoring the quarterly knowledge-base
/// refresh pipeline (US_078 AC-3, AIR-R05).
///
/// Routes:
///   POST /api/admin/knowledge-base/refresh         — Trigger a refresh (Admin only).
///   GET  /api/admin/knowledge-base/refresh/status  — Poll last refresh result.
///
/// Authorization (OWASP A01):
///   Both endpoints require <see cref="RbacPolicies.AdminOnly"/>.
///   Non-admin callers receive 403 Forbidden.
///
/// Audit logging (AIR-S04):
///   On trigger the admin user ID, source version, entry count, and correlation ID
///   are written to structured logs.  No code descriptions or patient data are logged.
///
/// Response contract:
///   POST returns 202 Accepted with the completed <see cref="KbRefreshResultDto"/> once
///   the pipeline finishes.  (Synchronous refresh — suitable for quarterly cadence where
///   duration is acceptable.  Async job pattern can be layered on later if needed.)
///   On service failure (Status = Failed) the endpoint returns 422 Unprocessable Entity
///   with the error message.
/// </summary>
[ApiVersion("1.0")]
[ApiController]
[Authorize(Policy = RbacPolicies.AdminOnly)]
[Route("api/admin/knowledge-base")]
[Produces("application/json")]
public sealed class KnowledgeBaseRefreshController : ControllerBase
{
    private readonly IKnowledgeBaseRefreshService          _refreshService;
    private readonly ILogger<KnowledgeBaseRefreshController> _logger;

    public KnowledgeBaseRefreshController(
        IKnowledgeBaseRefreshService              refreshService,
        ILogger<KnowledgeBaseRefreshController>   logger)
    {
        _refreshService = refreshService;
        _logger         = logger;
    }

    // ── POST /api/admin/knowledge-base/refresh ────────────────────────────────

    /// <summary>Triggers a quarterly knowledge-base refresh for a single embedding category.</summary>
    /// <param name="dto">Refresh payload (entries, category, version).</param>
    /// <param name="ct">Request cancellation token.</param>
    /// <returns>Refresh outcome with counts and duration.</returns>
    [HttpPost("refresh")]
    [ProducesResponseType(typeof(KbRefreshResultDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse),       StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ErrorResponse),       StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RefreshAsync(
        [FromBody] KbRefreshRequestDto dto,
        CancellationToken ct)
    {
        // ── Input validation ──────────────────────────────────────────────────

        // Validate allowed code systems (guard against unexpected values, OWASP A03).
        var invalidSystems = dto.Entries
            .Where(e => !IsAllowedCodeSystem(e.CodeSystem))
            .Select(e => e.CodeSystem)
            .Distinct()
            .ToList();

        if (invalidSystems.Count > 0)
        {
            return BadRequest(BuildError(400,
                $"Unsupported CodeSystem value(s): {string.Join(", ", invalidSystems)}. " +
                "Accepted values: ICD-10, CPT."));
        }

        var userId        = GetCurrentUserId();
        var correlationId = GetCorrelationId();

        // Audit log the trigger (AIR-S04): user ID, version, entry count — no code text.
        _logger.LogInformation(
            "KbRefreshController: refresh triggered. Category={Category} Version={Version} " +
            "EntryCount={Count} AdminUserId={UserId} CorrelationId={CorrelationId}",
            dto.TargetCategory, dto.SourceVersion, dto.Entries.Count,
            userId, correlationId);

        // ── Map DTO → service request ─────────────────────────────────────────

        var serviceRequest = new KbRefreshRequest
        {
            Entries = dto.Entries
                .Select(e => new CodeLibraryEntry
                {
                    CodeValue    = e.CodeValue,
                    CodeSystem   = e.CodeSystem,
                    Description  = e.Description,
                    Category     = dto.TargetCategory,
                    IsDeprecated = e.IsDeprecated,
                })
                .ToList(),
            TargetCategory     = dto.TargetCategory,
            SourceVersion      = dto.SourceVersion,
            InitiatedByUserId  = userId?.ToString() ?? "unknown",
        };

        // ── Execute ───────────────────────────────────────────────────────────

        var result = await _refreshService.RefreshAsync(serviceRequest, ct);

        var resultDto = ToDto(result);

        if (result.Status == RefreshStatus.Failed)
        {
            _logger.LogWarning(
                "KbRefreshController: refresh failed. Category={Category} Version={Version} " +
                "Error={Error} CorrelationId={CorrelationId}",
                dto.TargetCategory, dto.SourceVersion, result.ErrorMessage, correlationId);

            return UnprocessableEntity(resultDto);
        }

        _logger.LogInformation(
            "KbRefreshController: refresh completed. Category={Category} New={New} " +
            "Updated={Updated} Deprecated={Deprecated} Duration={Duration}ms " +
            "CorrelationId={CorrelationId}",
            dto.TargetCategory, result.NewCodesAdded, result.CodesUpdated,
            result.CodesDeprecated, result.Duration.TotalMilliseconds, correlationId);

        return Accepted(resultDto);
    }

    // ── GET /api/admin/knowledge-base/refresh/status ──────────────────────────

    /// <summary>Returns the last known refresh result for admin status polling.</summary>
    [HttpGet("refresh/status")]
    [ProducesResponseType(typeof(KbRefreshResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetStatusAsync(CancellationToken ct)
    {
        var result = await _refreshService.GetRefreshStatusAsync(ct);

        if (result is null)
            return NoContent();

        return Ok(ToDto(result));
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static readonly HashSet<string> AllowedCodeSystems =
        new(StringComparer.OrdinalIgnoreCase) { "ICD-10", "CPT" };

    private static bool IsAllowedCodeSystem(string codeSystem)
        => AllowedCodeSystems.Contains(codeSystem);

    private static KbRefreshResultDto ToDto(KbRefreshResult r) => new()
    {
        NewCodesAdded   = r.NewCodesAdded,
        CodesUpdated    = r.CodesUpdated,
        CodesDeprecated = r.CodesDeprecated,
        TotalProcessed  = r.TotalProcessed,
        Duration        = r.Duration.ToString(@"hh\:mm\:ss\.fff"),
        Status          = r.Status.ToString(),
        ErrorMessage    = r.ErrorMessage,
    };

    private string GetCorrelationId()
        => HttpContext.Items[CorrelationIdMiddleware.ItemsKey]?.ToString()
           ?? Guid.NewGuid().ToString();

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
               ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private ErrorResponse BuildError(int statusCode, string message) => new()
    {
        StatusCode    = statusCode,
        Message       = message,
        CorrelationId = GetCorrelationId(),
        Timestamp     = DateTimeOffset.UtcNow,
    };
}
