using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPACIP.Api.Authorization;
using UPACIP.Api.Middleware;
using UPACIP.Api.Models;
using UPACIP.Service.Profile;
using Asp.Versioning;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Staff-facing patient search and provider list endpoints (US_062 AC-1, SCR-016).
///
/// Endpoints:
///   GET /api/staff/patients/search   — paginated fuzzy patient search with provider/status filters.
///   GET /api/staff/providers/list    — distinct provider names for the search filter dropdown.
///
/// Authorization: Staff or Admin role only (OWASP A01).
///
/// Caching (NFR-030):
///   Both endpoints apply a 5-minute Redis cache-aside delegated to
///   <see cref="IPatientSearchService"/>. Audit entries are always written
///   regardless of cache hit (NFR-012).
/// </summary>
[ApiVersion("1.0")]
[ApiController]
[Route("api/staff")]
[Authorize(Policy = RbacPolicies.StaffOrAdmin)]
[Produces("application/json")]
public sealed class PatientSearchController : ControllerBase
{
    private readonly IPatientSearchService            _searchService;
    private readonly ILogger<PatientSearchController> _logger;

    public PatientSearchController(
        IPatientSearchService            searchService,
        ILogger<PatientSearchController> logger)
    {
        _searchService = searchService;
        _logger        = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/staff/patients/search
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a paginated list of patients matching the supplied search criteria.
    ///
    /// The <paramref name="q"/> parameter is matched case-insensitively (ILike) against
    /// the patient's full name, phone number, and email address.
    /// Supply <paramref name="provider"/> (a provider GUID string) to restrict results to
    /// patients with at least one appointment assigned to that provider.
    /// Supply <paramref name="status"/> as "Active", "Inactive", or "All" (default "All").
    /// </summary>
    /// <param name="q">Search term (minimum 2 characters).</param>
    /// <param name="provider">Optional provider ID filter.</param>
    /// <param name="status">"Active" | "Inactive" | "All". Defaults to "All".</param>
    /// <param name="page">1-based page index. Defaults to 1.</param>
    /// <param name="pageSize">Items per page (1–100). Defaults to 20.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Paginated search results returned successfully.</response>
    /// <response code="400">Search term is missing or fewer than 2 characters.</response>
    [HttpGet("patients/search")]
    [ProducesResponseType(typeof(PatientSearchResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchPatients(
        [FromQuery] string  q          = "",
        [FromQuery] string  provider   = "",
        [FromQuery] string  status     = "All",
        [FromQuery] int     page       = 1,
        [FromQuery] int     pageSize   = 20,
        CancellationToken   ct         = default)
    {
        // Allow empty term (returns all patients for browsing).
        // When a term is supplied it must be at least 2 chars to be meaningful.
        if (!string.IsNullOrWhiteSpace(q) && q.Trim().Length < 2)
            return BadRequest(BuildError(400, "Search term must be at least 2 characters."));

        var userId = GetCurrentUserId();
        if (userId is null)
            return Unauthorized(BuildError(401, "Unable to identify the current user."));

        var query = new PatientSearchQuery(
            Term:     q.Trim(),
            Provider: provider.Trim(),
            Status:   status.Trim(),
            Page:     page,
            PageSize: pageSize);

        try
        {
            var result = await _searchService.SearchPatientsAsync(
                query,
                actingUserId: userId.Value,
                ipAddress:    HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty,
                userAgent:    Request.Headers.UserAgent.ToString(),
                ct:           ct);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "PatientSearchController: search failed. UserId={UserId} Term={Term}",
                userId, q);
            return StatusCode(StatusCodes.Status500InternalServerError,
                BuildError(500, "An error occurred while processing the patient search."));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/staff/providers/list
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the list of distinct provider names available as search filter options.
    /// Results are sourced from the appointments table and cached for 5 minutes (NFR-030).
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Provider list returned successfully.</response>
    [HttpGet("providers/list")]
    [ProducesResponseType(typeof(IReadOnlyList<ProviderOptionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetProviderList(CancellationToken ct = default)
    {
        var providers = await _searchService.GetProviderListAsync(ct);
        return Ok(providers);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
               ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private ErrorResponse BuildError(int statusCode, string message)
    {
        var correlationId = HttpContext.Items.TryGetValue(CorrelationIdMiddleware.ItemsKey, out var v)
            ? v?.ToString() ?? Guid.NewGuid().ToString()
            : Guid.NewGuid().ToString();

        return new ErrorResponse
        {
            StatusCode    = statusCode,
            Message       = message,
            CorrelationId = correlationId,
            Timestamp     = DateTimeOffset.UtcNow,
        };
    }
}
