using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StackExchange.Redis;
using System.Text.Json;
using UPACIP.Api.Authorization;
using UPACIP.Api.Middleware;
using UPACIP.Api.Models;
using UPACIP.Service.Coding;

namespace UPACIP.Api.Controllers;

/// <summary>
/// REST endpoints for CPT procedure code review and library management (US_048, AC-1, AC-3, AC-4, FR-066).
///
/// Routes:
///   POST   /api/coding/cpt/generate              — Enqueue AI-driven CPT mapping job (Staff/Admin).
///   GET    /api/coding/cpt/pending/{patientId}    — Retrieve pending CPT codes for a patient (Staff/Admin).
///   PUT    /api/coding/cpt/approve                — Approve an AI-suggested CPT code (Staff/Admin).
///   PUT    /api/coding/cpt/override               — Override an incorrect AI suggestion (Staff/Admin).
///   PUT    /api/coding/cpt/library/refresh        — Apply quarterly CPT library update (Admin only).
///   POST   /api/coding/cpt/library/revalidate     — Revalidate pending codes against current library (Admin only).
///
/// Authorization (OWASP A01 — Broken Access Control):
///   - Pending reads and review actions (approve/override) are restricted to Staff/Admin.
///   - Library management operations (refresh/revalidate) are restricted to Admin only.
///   - The acting user's ID is always read from the JWT bearer token; it is never trusted
///     from the request body to prevent privilege escalation attacks.
///
/// IpAddress / UserAgent:
///   Both are captured from <c>HttpContext</c> for HIPAA audit trail completeness (§164.312(b)).
///   IpAddress uses the last hop (<c>RemoteIpAddress</c>); reverse-proxy forwarded headers are
///   handled by ASP.NET Core's ForwardedHeaders middleware configured in Program.cs.
/// </summary>
[ApiController]
[Authorize(Policy = RbacPolicies.StaffOrAdmin)]
[Produces("application/json")]
public sealed class CptCodingController : ControllerBase
{
    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly TimeSpan IdempotencyWindowTtl = TimeSpan.FromHours(24); // NFR-034

    private static readonly JsonSerializerOptions QueueJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly IConnectionMultiplexer         _redis;
    private readonly ICptCodingService              _codingService;
    private readonly ICptCodeLibraryService         _libraryService;
    private readonly ILogger<CptCodingController>   _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public CptCodingController(
        IConnectionMultiplexer        redis,
        ICptCodingService             codingService,
        ICptCodeLibraryService        libraryService,
        ILogger<CptCodingController>  logger)
    {
        _redis          = redis;
        _codingService  = codingService;
        _libraryService = libraryService;
        _logger         = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/coding/cpt/generate
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Enqueues a CPT procedure code generation job for the given patient procedures (US_048, AC-1, AC-3).
    ///
    /// Returns 202 Accepted immediately; the <see cref="CptCodingWorker"/> background
    /// service processes the job asynchronously (NFR-029, AIR-O07).
    ///
    /// Idempotency (NFR-034): when <c>idempotency_key</c> is supplied the controller
    /// performs an atomic Redis SET NX to prevent duplicate job enqueue on client retry.
    /// On Redis failure the pipeline degrades gracefully — the job is enqueued without
    /// deduplication (NFR-030).
    /// </summary>
    [HttpPost("api/coding/cpt/generate")]
    [EnableRateLimiting("cpt-generate-limit")]
    [ProducesResponseType(typeof(CptGenerateAcceptedDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GenerateAsync(
        [FromBody] CptGenerateRequestDto request,
        CancellationToken ct = default)
    {
        var correlationId = GetCorrelationId();

        var job = new CptCodingJob
        {
            PatientId     = request.PatientId,
            ProcedureIds  = request.ProcedureIds,
            CorrelationId = correlationId,
        };

        // ── Atomic idempotency claim (NFR-034) ───────────────────────────────
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var dedupeKey = $"upacip:cpt:idempotency:{request.PatientId}:{request.IdempotencyKey}";

            try
            {
                var db = _redis.GetDatabase();

                var claimed = await db.StringSetAsync(
                    dedupeKey,
                    job.JobId.ToString(),
                    IdempotencyWindowTtl,
                    When.NotExists);

                if (!claimed)
                {
                    var existingRaw = await db.StringGetAsync(dedupeKey);
                    var existingId  = existingRaw.IsNullOrEmpty || !Guid.TryParse(existingRaw, out var parsed)
                        ? job.JobId
                        : parsed;

                    _logger.LogInformation(
                        "CptCodingController: idempotent replay — returning existing job. " +
                        "PatientId={PatientId} IdempotencyKey={Key} ExistingJobId={JobId} " +
                        "CorrelationId={CorrelationId}",
                        request.PatientId, request.IdempotencyKey, existingId, correlationId);

                    return Accepted(new CptGenerateAcceptedDto
                    {
                        JobId      = existingId,
                        PatientId  = request.PatientId,
                        AcceptedAt = DateTime.UtcNow,
                        Message    = "Duplicate request — original job ID returned.",
                    });
                }
            }
            catch (Exception ex) when (ex is RedisException or RedisTimeoutException or RedisConnectionException)
            {
                _logger.LogWarning(ex,
                    "CptCodingController: Redis unavailable during idempotency claim. " +
                    "Proceeding without deduplication. CorrelationId={CorrelationId}", correlationId);
            }
        }

        // ── Enqueue job ──────────────────────────────────────────────────────
        var jobJson = JsonSerializer.Serialize(job, QueueJsonOptions);

        try
        {
            await _redis.GetDatabase().ListRightPushAsync(CptCodingWorker.QueueKey, jobJson);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException or RedisConnectionException)
        {
            _logger.LogError(ex,
                "CptCodingController: Redis unavailable — cannot enqueue CPT coding job. " +
                "PatientId={PatientId} CorrelationId={CorrelationId}",
                request.PatientId, correlationId);

            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                var dedupeKey = $"upacip:cpt:idempotency:{request.PatientId}:{request.IdempotencyKey}";
                try { await _redis.GetDatabase().KeyDeleteAsync(dedupeKey); } catch { /* best-effort */ }
            }

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                BuildError(503, "CPT coding queue is temporarily unavailable. Please retry shortly."));
        }

        _logger.LogInformation(
            "CptCodingController: CPT coding job enqueued. JobId={JobId} PatientId={PatientId} " +
            "ProcedureCount={Count} CorrelationId={CorrelationId}",
            job.JobId, request.PatientId, request.ProcedureIds.Count, correlationId);

        return Accepted(new CptGenerateAcceptedDto
        {
            JobId      = job.JobId,
            PatientId  = request.PatientId,
            AcceptedAt = job.EnqueuedAt,
            Message    = "CPT coding job accepted. Results will be available via GET /api/coding/cpt/pending/{patientId}.",
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/coding/cpt/pending/{patientId}
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns pending (unapproved) CPT codes for a patient, sorted by relevance rank (US_048, AC-1, AC-3).
    ///
    /// Results are cached for 5 minutes (NFR-030).
    /// The AI generation pipeline (task_004) will populate these rows; until that task is
    /// complete the response will contain an empty <c>codes</c> array for new patients.
    /// </summary>
    [HttpGet("api/coding/cpt/pending/{patientId:guid}")]
    [ProducesResponseType(typeof(CptMappingResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPendingAsync(
        [FromRoute] Guid patientId,
        CancellationToken ct = default)
    {
        if (patientId == Guid.Empty)
            return BadRequest(BuildError(400, "patientId is required."));

        var correlationId = GetCorrelationId();

        var codes = await _codingService.GetPendingCodesAsync(patientId, ct);

        var codeDtos = codes.Select(c => new CptCodeDto
        {
            MedicalCodeId   = c.Id,
            CodeValue       = c.CodeValue,
            Description     = c.Description,
            ConfidenceScore = c.AiConfidenceScore ?? 0f,
            Justification   = c.Justification,
            RelevanceRank   = c.RelevanceRank,
            Status          = DeriveStatus(c),
            IsBundled       = c.IsBundled,
            BundleGroupId   = c.BundleGroupId,
            LibraryVersion  = c.LibraryVersion,
            ValidationStatus = c.RevalidationStatus?.ToString(),
        }).ToList();

        var lastRunAt = codes.Count > 0 ? codes.Max(c => c.CreatedAt) : (DateTime?)null;

        _logger.LogDebug(
            "CptCodingController: pending codes retrieved. PatientId={PatientId} Count={Count} CorrelationId={CorrelationId}",
            patientId, codeDtos.Count, correlationId);

        return Ok(new CptMappingResponseDto
        {
            PatientId       = patientId,
            Codes           = codeDtos,
            LastCodingRunAt = lastRunAt,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/coding/cpt/approve
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Approves an AI-suggested CPT code (US_048, AC-1).
    ///
    /// The acting user's ID is read from the JWT bearer token (OWASP A01 — never from request body).
    /// Writes a <c>CptCodeApproved</c> audit entry (HIPAA §164.312(b)).
    /// </summary>
    [HttpPut("api/coding/cpt/approve")]
    [ProducesResponseType(typeof(CptApproveResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ApproveAsync(
        [FromBody] CptApproveRequest request,
        CancellationToken ct = default)
    {
        var correlationId = GetCorrelationId();

        // OWASP A01: user identity always from JWT — never from request body.
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            _logger.LogWarning(
                "CptCodingController: ApproveAsync — could not resolve user ID from JWT. " +
                "CorrelationId={CorrelationId}", correlationId);
            return Unauthorized(BuildError(401, "User identity could not be resolved."));
        }

        try
        {
            var updated = await _codingService.ApproveCptCodeAsync(
                request.MedicalCodeId,
                userId.Value,
                correlationId,
                ct);

            _logger.LogInformation(
                "CptCodingController: CPT code approved. MedicalCodeId={MedicalCodeId} UserId={UserId} CorrelationId={CorrelationId}",
                request.MedicalCodeId, userId, correlationId);

            return Ok(new CptApproveResultDto
            {
                MedicalCodeId = updated.Id,
                CodeValue     = updated.CodeValue,
                ApprovedAt    = updated.UpdatedAt,
                Message       = $"CPT code '{updated.CodeValue}' approved successfully.",
            });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex,
                "CptCodingController: CPT code not found for approval. " +
                "MedicalCodeId={MedicalCodeId} CorrelationId={CorrelationId}",
                request.MedicalCodeId, correlationId);

            return NotFound(BuildError(404, ex.Message));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/coding/cpt/override
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Overrides an AI-suggested CPT code with a clinically correct alternative (US_048, edge case).
    ///
    /// The acting user's ID is read from the JWT bearer token (OWASP A01).
    /// The justification text is stored in <c>MedicalCode.Justification</c> for HIPAA auditability.
    /// Writes a <c>CptCodeOverridden</c> audit entry (HIPAA §164.312(b)).
    /// </summary>
    [HttpPut("api/coding/cpt/override")]
    [ProducesResponseType(typeof(CptOverrideResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> OverrideAsync(
        [FromBody] CptOverrideRequest request,
        CancellationToken ct = default)
    {
        var correlationId = GetCorrelationId();

        // OWASP A01: user identity always from JWT — never from request body.
        var userId = GetCurrentUserId();
        if (userId is null)
        {
            _logger.LogWarning(
                "CptCodingController: OverrideAsync — could not resolve user ID from JWT. " +
                "CorrelationId={CorrelationId}", correlationId);
            return Unauthorized(BuildError(401, "User identity could not be resolved."));
        }

        try
        {
            var updated = await _codingService.OverrideCptCodeAsync(
                request.MedicalCodeId,
                request.ReplacementCode,
                request.Justification,
                userId.Value,
                correlationId,
                ct);

            _logger.LogInformation(
                "CptCodingController: CPT code overridden. MedicalCodeId={MedicalCodeId} " +
                "ReplacementCode={Code} UserId={UserId} CorrelationId={CorrelationId}",
                request.MedicalCodeId, request.ReplacementCode, userId, correlationId);

            return Ok(new CptOverrideResultDto
            {
                MedicalCodeId   = updated.Id,
                ReplacementCode = updated.CodeValue,
                OverriddenAt    = updated.UpdatedAt,
                Message         = $"CPT code overridden with '{updated.CodeValue}'.",
            });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex,
                "CptCodingController: CPT code not found for override. " +
                "MedicalCodeId={MedicalCodeId} CorrelationId={CorrelationId}",
                request.MedicalCodeId, correlationId);

            return NotFound(BuildError(404, ex.Message));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/coding/cpt/library/refresh  (Admin only)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies a quarterly CPT library update (US_048, AC-4).
    ///
    /// Restricted to the Admin role — library writes affect the validation baseline for
    /// all patients system-wide (OWASP A01).
    ///
    /// The operation is transactional: a partial failure rolls back the entire update
    /// so the library is never left in an inconsistent state (DR-029).
    /// </summary>
    [HttpPut("api/coding/cpt/library/refresh")]
    [Authorize(Policy = RbacPolicies.AdminOnly)]
    [ProducesResponseType(typeof(CptLibraryRefreshResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RefreshLibraryAsync(
        [FromBody] CptLibraryRefreshRequestDto request,
        CancellationToken ct = default)
    {
        var correlationId = GetCorrelationId();

        _logger.LogInformation(
            "CptCodingController: library refresh requested. Version={Version} CodeCount={Count} CorrelationId={CorrelationId}",
            request.Version, request.Codes.Count, correlationId);

        var entries = request.Codes.Select(c => new CptCodeEntry
        {
            CptCode        = c.CptCode,
            Description    = c.Description,
            Category       = c.Category,
            EffectiveDate  = c.EffectiveDate,
            ExpirationDate = c.ExpirationDate,
        }).ToList();

        var result = await _libraryService.RefreshLibraryAsync(request.Version, entries, correlationId, ct);

        return Ok(new CptLibraryRefreshResultDto
        {
            Version                 = result.Version,
            CodesAdded              = result.CodesAdded,
            CodesDeactivated        = result.CodesDeactivated,
            PendingCodesRevalidated = result.PendingCodesRevalidated,
            RefreshedAt             = result.RefreshedAt,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/coding/cpt/library/revalidate  (Admin only)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Triggers a standalone revalidation of all pending CPT <c>MedicalCode</c> records
    /// against the current CPT library (US_048, AC-4, edge case).
    ///
    /// Typically invoked after an out-of-band library patch.  Restricted to Admin role (OWASP A01).
    /// </summary>
    [HttpPost("api/coding/cpt/library/revalidate")]
    [Authorize(Policy = RbacPolicies.AdminOnly)]
    [ProducesResponseType(typeof(CptRevalidationResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RevalidateAsync(CancellationToken ct = default)
    {
        var correlationId = GetCorrelationId();

        _logger.LogInformation(
            "CptCodingController: standalone revalidation triggered. CorrelationId={CorrelationId}",
            correlationId);

        var result = await _libraryService.RevalidatePendingCodesAsync(correlationId, ct);

        return Ok(new CptRevalidationResultDto
        {
            TotalExamined     = result.TotalExamined,
            MarkedValid       = result.MarkedValid,
            MarkedInvalid     = result.MarkedInvalid,
            MarkedPendingReview = result.MarkedPendingReview,
            RevalidatedAt     = result.RevalidatedAt,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private string GetCorrelationId()
        => HttpContext.Items[CorrelationIdMiddleware.ItemsKey]?.ToString()
           ?? Guid.NewGuid().ToString();

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
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

    /// <summary>
    /// Derives the <c>Status</c> string for a CPT <c>MedicalCode</c> row.
    /// "Pending" = unapproved; "Approved" = approved and unchanged; "Overridden" = approved with override.
    ///
    /// Note: There is currently no dedicated <c>IsOverridden</c> flag on <c>MedicalCode</c>.
    /// Override is inferred from: ApprovedByUserId is set AND the SuggestedByAi flag is true AND
    /// the Justification contains the staff-entered text (set during override).  The definitive
    /// approach — adding an <c>IsOverridden</c> column — is tracked for task_003_db.
    /// </summary>
    private static string DeriveStatus(DataAccess.Entities.MedicalCode code)
    {
        if (code.ApprovedByUserId is null)
            return "Pending";

        // Heuristic: if the code was AI-suggested and the justification was explicitly
        // provided by staff (non-empty, not the AI-generated one), it was overridden.
        // Full status discrimination deferred to task_003_db IsOverridden column.
        return "Approved";
    }
}
