using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using StackExchange.Redis;
using UPACIP.Api.Authorization;
using UPACIP.Api.Middleware;
using UPACIP.Api.Models;
using UPACIP.Service.Coding;

namespace UPACIP.Api.Controllers;

/// <summary>
/// REST endpoints for ICD-10 diagnosis code mapping (US_047, AC-1, AC-3, AC-4, FR-063).
///
/// Routes:
///   POST   /api/coding/icd10/generate             — Enqueue AI-driven ICD-10 mapping job (Staff/Admin).
///   GET    /api/coding/icd10/pending               — Retrieve pending codes for a patient (Staff/Admin).
///   POST   /api/coding/icd10/library/refresh       — Apply quarterly library update (Admin only).
///   POST   /api/coding/icd10/library/revalidate    — Revalidate pending codes against current library (Admin only).
///
/// Authorization (OWASP A01 — Broken Access Control):
///   - Generation and pending reads are restricted to Staff/Admin.
///   - Library management operations are restricted to Admin only.
///   - The current user ID for audit attribution is always read from the JWT; never
///     trusted from request body.
///
/// Async processing (NFR-029, AIR-O07):
///   POST generate enqueues a job to the Redis list <c>upacip:icd10-coding-queue</c> and
///   returns 202 Accepted immediately.  The <see cref="Icd10CodingWorker"/> drains the queue.
///
/// Rate limiting (AIR-S08):
///   The generate endpoint is guarded by the <c>icd10-generate-limit</c> policy:
///   100 requests per authenticated user per hour using a sliding-window limiter.
/// </summary>
[ApiController]
[Authorize(Policy = RbacPolicies.StaffOrAdmin)]
[Produces("application/json")]
public sealed class CodingController : ControllerBase
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions QueueJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented        = false,
    };

    // 24-hour idempotency window for the Redis de-duplication key (NFR-034).
    private static readonly TimeSpan IdempotencyWindowTtl = TimeSpan.FromHours(24);

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly IConnectionMultiplexer         _redis;
    private readonly IIcd10CodingService             _codingService;
    private readonly IIcd10LibraryService            _libraryService;
    private readonly ILogger<CodingController>       _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public CodingController(
        IConnectionMultiplexer    redis,
        IIcd10CodingService       codingService,
        IIcd10LibraryService      libraryService,
        ILogger<CodingController> logger)
    {
        _redis          = redis;
        _codingService  = codingService;
        _libraryService = libraryService;
        _logger         = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/coding/icd10/generate
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Enqueues an ICD-10 code generation job for the given patient diagnoses (US_047, AC-1, AC-4).
    ///
    /// Returns 202 Accepted immediately; the <see cref="Icd10CodingWorker"/> background
    /// service processes the job asynchronously (NFR-029, AIR-O07).
    ///
    /// Idempotency (NFR-034): when <c>idempotency_key</c> is supplied the controller
    /// performs an atomic Redis SET NX (set-if-not-exists) to claim the key before
    /// enqueuing.  This eliminates the TOCTOU race condition that would arise from a
    /// non-atomic GET-then-SET pattern.  On Redis failure the pipeline degrades
    /// gracefully — the job is enqueued without deduplication (NFR-030).
    ///
    /// Race-condition semantics:
    /// <list type="number">
    ///   <item>A new job ID is generated upfront.</item>
    ///   <item>
    ///     <see cref="When.NotExists"/> SET atomically claims the key.
    ///     If it returns <c>false</c> the key was already owned by a previous request;
    ///     the existing job ID is retrieved and returned without re-enqueuing.
    ///   </item>
    ///   <item>
    ///     If the queue push subsequently fails the key is deleted so the caller may
    ///     safely retry (the key is not left in a permanently-claimed state).
    ///   </item>
    /// </list>
    /// </summary>
    [HttpPost("api/coding/icd10/generate")]
    [EnableRateLimiting("icd10-generate-limit")]
    [ProducesResponseType(typeof(Icd10MappingAcceptedDto), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> GenerateAsync(
        [FromBody] Icd10MappingRequestDto request,
        CancellationToken ct = default)
    {
        var correlationId = GetCorrelationId();

        // Build the job upfront so the job ID is available for idempotency storage.
        var job = new Icd10CodingJob
        {
            PatientId     = request.PatientId,
            DiagnosisIds  = request.DiagnosisIds,
            CorrelationId = correlationId,
        };

        // ── Atomic idempotency claim (NFR-034) ───────────────────────────────
        // Uses Redis SET NX to atomically claim the deduplication key.
        // This avoids the TOCTOU race between a non-atomic GET-then-SET approach.
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var dedupeKey = $"upacip:icd10:idempotency:{request.PatientId}:{request.IdempotencyKey}";

            try
            {
                var db = _redis.GetDatabase();

                // Try to atomically claim the key with the new job ID.
                var claimed = await db.StringSetAsync(
                    dedupeKey,
                    job.JobId.ToString(),
                    IdempotencyWindowTtl,
                    When.NotExists);          // SET NX — atomic; returns true only when key did not exist

                if (!claimed)
                {
                    // Key already existed — another (earlier) request owns it.
                    var existingRaw = await db.StringGetAsync(dedupeKey);
                    var existingId  = existingRaw.IsNullOrEmpty || !Guid.TryParse(existingRaw, out var parsed)
                        ? job.JobId   // key expired between SET NX and GET (extremely unlikely) — treat as new
                        : parsed;

                    _logger.LogInformation(
                        "CodingController: idempotent replay — returning existing job. " +
                        "PatientId={PatientId} IdempotencyKey={Key} ExistingJobId={JobId} " +
                        "CorrelationId={CorrelationId}",
                        request.PatientId, request.IdempotencyKey, existingId, correlationId);

                    return Accepted(new Icd10MappingAcceptedDto
                    {
                        JobId      = existingId,
                        PatientId  = request.PatientId,
                        AcceptedAt = DateTime.UtcNow,
                        Message    = "Duplicate request — original job ID returned.",
                    });
                }
                // claimed == true: we own the key; proceed to enqueue.
                // If the enqueue fails below, we delete the key so the caller can retry.
            }
            catch (Exception ex) when (ex is RedisException or RedisTimeoutException or RedisConnectionException)
            {
                // Redis failure must not block the coding pipeline — degrade gracefully (NFR-030).
                _logger.LogWarning(ex,
                    "CodingController: Redis unavailable during idempotency claim. " +
                    "Proceeding without deduplication. CorrelationId={CorrelationId}", correlationId);
            }
        }

        // ── Enqueue job ──────────────────────────────────────────────────────
        var jobJson = JsonSerializer.Serialize(job, QueueJsonOptions);

        try
        {
            await _redis.GetDatabase().ListRightPushAsync(Icd10CodingWorker.QueueKey, jobJson);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException or RedisConnectionException)
        {
            _logger.LogError(ex,
                "CodingController: Redis unavailable — cannot enqueue coding job. " +
                "PatientId={PatientId} CorrelationId={CorrelationId}",
                request.PatientId, correlationId);

            // Release the idempotency claim so the caller can safely retry (NFR-034 edge case).
            if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
            {
                var dedupeKey = $"upacip:icd10:idempotency:{request.PatientId}:{request.IdempotencyKey}";
                try { await _redis.GetDatabase().KeyDeleteAsync(dedupeKey); } catch { /* best-effort */ }
            }

            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                BuildError(503, "Coding queue is temporarily unavailable. Please retry shortly."));
        }

        _logger.LogInformation(
            "CodingController: coding job enqueued. JobId={JobId} PatientId={PatientId} " +
            "DiagnosisCount={Count} CorrelationId={CorrelationId}",
            job.JobId, request.PatientId, request.DiagnosisIds.Count, correlationId);

        return Accepted(new Icd10MappingAcceptedDto
        {
            JobId      = job.JobId,
            PatientId  = request.PatientId,
            AcceptedAt = job.EnqueuedAt,
            Message    = "Coding job accepted. Results will be available via GET /api/coding/icd10/pending.",
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/coding/icd10/pending
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns pending (unapproved) ICD-10 codes for a patient, sorted by relevance rank (US_047, AC-1, AC-4).
    ///
    /// Results are cached for 5 minutes (NFR-030).
    /// Includes both valid suggestions and <c>"UNCODABLE"</c> sentinel entries that require
    /// manual coding (edge case).
    /// </summary>
    [HttpGet("api/coding/icd10/pending")]
    [ProducesResponseType(typeof(Icd10MappingResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPendingAsync(
        [FromQuery] Guid patientId,
        CancellationToken ct = default)
    {
        if (patientId == Guid.Empty)
            return BadRequest(BuildError(400, "patientId is required."));

        var correlationId = GetCorrelationId();

        var codes = await _codingService.GetPendingCodesAsync(patientId, ct);

        var unmappedIds = codes
            .Where(c => c.CodeValue == "UNCODABLE")
            .Select(c => c.PatientId) // PatientId used as proxy — the real diagnosis link is in job context
            .Distinct()
            .ToList();

        var codeDtos = codes.Select(c => new Icd10CodeDto
        {
            MedicalCodeId   = c.Id,
            CodeValue       = c.CodeValue,
            Description     = c.Description,
            ConfidenceScore = c.AiConfidenceScore ?? 0f,
            Justification   = c.Justification,
            RelevanceRank   = c.RelevanceRank,
            ValidationStatus = c.RevalidationStatus?.ToString(),
            LibraryVersion  = c.LibraryVersion,
            RequiresReview  = c.RevalidationStatus == DataAccess.Enums.RevalidationStatus.DeprecatedReplaced
                           || c.CodeValue == "UNCODABLE",
        }).ToList();

        var lastRunAt = codes.Count > 0
            ? codes.Max(c => c.CreatedAt)
            : (DateTime?)null;

        _logger.LogDebug(
            "CodingController: pending codes retrieved. PatientId={PatientId} Count={Count} CorrelationId={CorrelationId}",
            patientId, codeDtos.Count, correlationId);

        return Ok(new Icd10MappingResponseDto
        {
            PatientId            = patientId,
            Codes                = codeDtos,
            UnmappedDiagnosisIds = [], // populated once diagnosis FK linkage is in place (task_003)
            LastCodingRunAt      = lastRunAt,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/coding/icd10/library/refresh  (Admin only)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies a quarterly ICD-10 library update (US_047, AC-3).
    ///
    /// Restricted to the Admin role — library writes affect the validation baseline for
    /// all patients system-wide (OWASP A01).
    ///
    /// The operation is transactional: a partial failure rolls back the entire update
    /// so the library is never left in an inconsistent state (DR-029).
    /// </summary>
    [HttpPost("api/coding/icd10/library/refresh")]
    [Authorize(Policy = RbacPolicies.AdminOnly)]
    [ProducesResponseType(typeof(LibraryRefreshResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RefreshLibraryAsync(
        [FromBody] LibraryRefreshRequestDto request,
        CancellationToken ct = default)
    {
        var correlationId = GetCorrelationId();

        _logger.LogInformation(
            "CodingController: library refresh requested. Version={Version} CodeCount={Count} CorrelationId={CorrelationId}",
            request.Version, request.Codes.Count, correlationId);

        var entries = request.Codes.Select(c => new Icd10CodeEntry
        {
            CodeValue       = c.CodeValue,
            Description     = c.Description,
            Category        = c.Category,
            EffectiveDate   = c.EffectiveDate,
            DeprecatedDate  = c.DeprecatedDate,
            ReplacementCode = c.ReplacementCode,
        }).ToList();

        var result = await _libraryService.RefreshLibraryAsync(request.Version, entries, correlationId, ct);

        return Ok(new LibraryRefreshResultDto
        {
            Version                  = result.Version,
            CodesAdded               = result.CodesAdded,
            CodesDeprecated          = result.CodesDeprecated,
            PendingCodesRevalidated  = result.PendingCodesRevalidated,
            DeprecatedRecordsFlagged = result.DeprecatedRecordsFlagged,
            RefreshedAt              = result.RefreshedAt,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/coding/icd10/library/revalidate  (Admin only)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Triggers a standalone revalidation of all pending <c>MedicalCode</c> records
    /// against the current ICD-10 library (US_047, AC-3, edge case).
    ///
    /// Typically invoked after an out-of-band library patch or when staff suspects
    /// the validation state is stale.  Restricted to Admin role (OWASP A01).
    /// </summary>
    [HttpPost("api/coding/icd10/library/revalidate")]
    [Authorize(Policy = RbacPolicies.AdminOnly)]
    [ProducesResponseType(typeof(RevalidationResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RevalidateAsync(CancellationToken ct = default)
    {
        var correlationId = GetCorrelationId();

        _logger.LogInformation(
            "CodingController: standalone revalidation triggered. CorrelationId={CorrelationId}",
            correlationId);

        var result = await _libraryService.RevalidatePendingCodesAsync(correlationId, ct);

        return Ok(new RevalidationResultDto
        {
            TotalExamined     = result.TotalExamined,
            MarkedValid       = result.MarkedValid,
            MarkedDeprecated  = result.MarkedDeprecated,
            MarkedPendingReview = result.MarkedPendingReview,
            RevalidatedAt     = result.RevalidatedAt,
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers (matching ConflictController pattern)
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
}
