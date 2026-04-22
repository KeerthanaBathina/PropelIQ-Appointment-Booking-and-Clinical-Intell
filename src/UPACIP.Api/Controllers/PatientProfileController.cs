using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UPACIP.Api.Authorization;
using UPACIP.Api.Middleware;
using UPACIP.Api.Models;
using UPACIP.DataAccess;
using UPACIP.Service.Profile;

namespace UPACIP.Api.Controllers;

/// <summary>
/// RESTful endpoints for the consolidated 360° patient profile (US_043, AC-1, AC-2, AC-3, FR-052, FR-056).
///
/// Endpoints:
///   GET  /api/patients/{patientId}/profile                                      — Full 360° profile with all clinical data categories.
///   GET  /api/patients/{patientId}/profile/versions                             — Ordered version history list.
///   GET  /api/patients/{patientId}/profile/versions/{versionNumber}             — Specific version snapshot.
///   GET  /api/patients/{patientId}/profile/data-points/{extractedDataId}/citation — Source document citation for a data point.
///   POST /api/patients/{patientId}/profile/consolidate                          — Trigger manual full consolidation.
///
/// Authorization: Staff or Admin role only (FR-002).
///
/// Caching (NFR-030):
///   The GET profile and GET versions endpoints apply a 5-minute Redis cache-aside,
///   delegated entirely to <see cref="IPatientProfileService"/>.
///   The POST consolidate endpoint invalidates both cache keys before returning.
///
/// OWASP A01 — Broken Access Control:
///   The <c>patientId</c> route parameter is validated against the database to confirm the
///   patient exists.  The citation endpoint additionally validates that the requested
///   <c>extractedDataId</c> belongs to the given <c>patientId</c> via a server-side join —
///   it is never trusted from the URL alone.
/// </summary>
[ApiController]
[Route("api/patients/{patientId:guid}/profile")]
[Authorize(Policy = RbacPolicies.StaffOrAdmin)]
[Produces("application/json")]
public sealed class PatientProfileController : ControllerBase
{
    private readonly IPatientProfileService            _profileService;
    private readonly ApplicationDbContext              _db;
    private readonly ILogger<PatientProfileController> _logger;

    public PatientProfileController(
        IPatientProfileService            profileService,
        ApplicationDbContext              db,
        ILogger<PatientProfileController> logger)
    {
        _profileService = profileService;
        _db             = db;
        _logger         = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/patients/{patientId}/profile
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the full 360° consolidated patient profile.
    ///
    /// Includes all active extracted clinical data (medications, diagnoses, procedures, allergies)
    /// with source document attribution, confidence scores, and review status for each data point.
    /// Results are served from Redis cache when available (5-minute TTL, NFR-030).
    /// </summary>
    /// <param name="patientId">The patient's unique identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Profile returned successfully. Includes all four data categories.</response>
    /// <response code="404">Patient not found or has been deleted.</response>
    [HttpGet]
    [ProducesResponseType(typeof(PatientProfile360Dto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetProfile(Guid patientId, CancellationToken ct)
    {
        var profile = await _profileService.GetProfile360Async(patientId, ct);
        if (profile is null)
            return NotFound(BuildError(404, $"Patient {patientId} not found."));

        return Ok(profile);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/patients/{patientId}/profile/versions
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the complete consolidation version history for a patient, newest version first.
    ///
    /// Each entry includes the version number, creation timestamp, triggering user, consolidation
    /// type (initial or incremental), and source document count (AC-2, FR-056).
    /// </summary>
    /// <param name="patientId">The patient's unique identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Version history returned. Empty array when no consolidation has run.</response>
    /// <response code="404">Patient not found.</response>
    [HttpGet("versions")]
    [ProducesResponseType(typeof(IReadOnlyList<VersionHistoryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVersionHistory(Guid patientId, CancellationToken ct)
    {
        if (!await PatientExistsAsync(patientId, ct))
            return NotFound(BuildError(404, $"Patient {patientId} not found."));

        var versions = await _profileService.GetVersionHistoryAsync(patientId, ct);
        return Ok(versions);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/patients/{patientId}/profile/versions/{versionNumber}
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the detail of a specific profile version including the data delta snapshot.
    ///
    /// The <c>dataSnapshot</c> field contains the JSONB delta of data points that changed
    /// during this consolidation event.  Null for versions with no net change (AC-2, FR-056).
    /// </summary>
    /// <param name="patientId">The patient's unique identifier.</param>
    /// <param name="versionNumber">The 1-based version number to retrieve.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Version detail returned.</response>
    /// <response code="404">Patient or version number not found.</response>
    [HttpGet("versions/{versionNumber:int}")]
    [ProducesResponseType(typeof(VersionHistoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetVersionSnapshot(Guid patientId, int versionNumber, CancellationToken ct)
    {
        var version = await _profileService.GetVersionSnapshotAsync(patientId, versionNumber, ct);
        if (version is null)
            return NotFound(BuildError(404, $"Version {versionNumber} not found for patient {patientId}."));

        return Ok(version);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/patients/{patientId}/profile/data-points/{extractedDataId}/citation
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the source document citation for a single extracted clinical data point (AC-3).
    ///
    /// The citation includes the document name, category, upload date, page number, extraction
    /// region, source snippet, and AI model attribution — enabling the staff UI to link a data
    /// point directly to the section of the original document it was extracted from.
    ///
    /// The data point ownership is validated server-side: if <paramref name="extractedDataId"/>
    /// does not belong to <paramref name="patientId"/> a 404 is returned (OWASP A01 guard).
    /// </summary>
    /// <param name="patientId">The patient's unique identifier.</param>
    /// <param name="extractedDataId">The ExtractedData row ID to look up.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Citation returned with full document details.</response>
    /// <response code="404">Data point not found or does not belong to this patient.</response>
    [HttpGet("data-points/{extractedDataId:guid}/citation")]
    [ProducesResponseType(typeof(SourceCitationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSourceCitation(
        Guid              patientId,
        Guid              extractedDataId,
        CancellationToken ct)
    {
        var citation = await _profileService.GetSourceCitationAsync(patientId, extractedDataId, ct);
        if (citation is null)
            return NotFound(BuildError(404,
                $"Citation not found for data point {extractedDataId} on patient {patientId}."));

        return Ok(citation);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/patients/{patientId}/profile/consolidate
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Triggers a full manual consolidation for the patient.
    ///
    /// Re-merges all completed document extractions into a new profile version,
    /// deduplicates, and invalidates the profile cache.  Use this after importing
    /// new documents or correcting data when the automated pipeline has not yet run.
    ///
    /// The triggering staff user ID is resolved server-side from the JWT sub claim.
    /// </summary>
    /// <param name="patientId">The patient's unique identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Consolidation completed. Returns the new version number and merge statistics.</response>
    /// <response code="404">Patient not found.</response>
    /// <response code="409">Consolidation failed — check the response message for details.</response>
    [HttpPost("consolidate")]
    [ProducesResponseType(typeof(ConsolidationResultResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> TriggerConsolidation(Guid patientId, CancellationToken ct)
    {
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            _logger.LogWarning(
                "PatientProfileController: could not resolve user ID from JWT. PatientId={PatientId}", patientId);
            return Unauthorized(BuildError(401, "Unable to resolve the requesting user identity."));
        }

        try
        {
            var result = await _profileService.TriggerConsolidationAsync(patientId, userId.Value, ct);
            return Ok(new ConsolidationResultResponse
            {
                PatientId               = result.PatientId,
                NewVersionNumber        = result.NewVersionNumber,
                TotalMergedCount        = result.TotalMergedCount,
                DuplicatesRemovedCount  = result.DuplicatesRemovedCount,
                NewDataPointsAddedCount = result.NewDataPointsAddedCount,
                DurationMs              = result.DurationMs,
                IsIncremental           = result.IsIncremental,
            });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(BuildError(404, ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "PatientProfileController: consolidation failed. PatientId={PatientId}", patientId);
            return Conflict(BuildError(409, "Consolidation failed. Please review system logs for details."));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    private async Task<bool> PatientExistsAsync(Guid patientId, CancellationToken ct)
        => await _db.Patients
            .AsNoTracking()
            .AnyAsync(p => p.Id == patientId && p.DeletedAt == null, ct);

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
               ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private ErrorResponse BuildError(int statusCode, string message)
    {
        var correlationId = HttpContext.Items[CorrelationIdMiddleware.ItemsKey]?.ToString()
                            ?? Guid.NewGuid().ToString();
        return new ErrorResponse
        {
            StatusCode    = statusCode,
            Message       = message,
            CorrelationId = correlationId,
            Timestamp     = DateTimeOffset.UtcNow,
        };
    }
}

/// <summary>
/// Lightweight response DTO for the manual consolidation trigger endpoint.
/// Exposes only the metrics a staff member needs to verify the operation succeeded.
/// </summary>
public sealed record ConsolidationResultResponse
{
    /// <summary>Patient whose profile was consolidated.</summary>
    public Guid PatientId { get; init; }

    /// <summary>The newly created profile version number.</summary>
    public int NewVersionNumber { get; init; }

    /// <summary>Total number of data points in the merged profile after deduplication.</summary>
    public int TotalMergedCount { get; init; }

    /// <summary>Number of duplicate data points removed during this consolidation run.</summary>
    public int DuplicatesRemovedCount { get; init; }

    /// <summary>Number of net-new data points added by this consolidation run.</summary>
    public int NewDataPointsAddedCount { get; init; }

    /// <summary>Wall-clock duration of the consolidation operation in milliseconds.</summary>
    public long DurationMs { get; init; }

    /// <summary>True when this was an incremental update; false for full re-consolidation.</summary>
    public bool IsIncremental { get; init; }
}
