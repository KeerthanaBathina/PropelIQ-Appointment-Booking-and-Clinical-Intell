using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using UPACIP.Api.Authorization;
using UPACIP.Api.Middleware;
using UPACIP.Api.Models;
using UPACIP.Service.Queue;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Arrival queue management endpoints for US_052 (AC-1 through AC-4).
///
/// Endpoints:
///   GET  /api/queue/today             — today's sorted queue with wait times (AC-4, UXR-103)
///   POST /api/queue/arrive            — mark patient arrived; 409 on duplicate (AC-1)
///   PUT  /api/queue/{queueId}/status  — update queue entry status; cancels slot (AC-3)
///   PUT  /api/queue/{queueId}/override — override no-show to arrived-late (edge case)
///
/// Authorization: Staff or Admin role (JWT).
///
/// Queue is cached in Redis with a 5-minute TTL (NFR-030, NFR-004). Any mutation
/// invalidates the cache key so the next GET re-fetches from the database.
///
/// Correlation IDs (TR-028): X-Correlation-ID header is echoed from
/// <see cref="CorrelationIdMiddleware"/> and forwarded to the service layer for structured
/// log tracing.
/// </summary>
[ApiController]
[Route("api/queue")]
[Authorize(Policy = RbacPolicies.StaffOrAdmin)]
[Produces("application/json")]
public sealed class QueueController : ControllerBase
{
    private readonly IQueueService             _queueService;
    private readonly ILogger<QueueController>  _logger;

    public QueueController(
        IQueueService            queueService,
        ILogger<QueueController> logger)
    {
        _queueService = queueService;
        _logger       = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/queue/today
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns today's queue entries sorted by priority (Urgent first) then appointment time.
    /// When filter or page parameters are supplied (US_053 dashboard), returns a paginated,
    /// filtered <see cref="QueuePagedResponseDto"/> with average wait time and threshold count.
    /// When called without parameters (legacy US_052 / internal use), returns the full list.
    /// Response is served from Redis cache (TTL 5 min) for sub-second latency (NFR-004).
    /// Auto-refresh interval on the frontend is 5 seconds (UXR-103).
    /// </summary>
    [HttpGet("today")]
    [ProducesResponseType(typeof(QueuePagedResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetTodayQueue(
        [FromQuery] string? provider,
        [FromQuery] string? appointmentType,
        [FromQuery] string? status,
        [FromQuery] int     page     = 1,
        [FromQuery] int     pageSize = 25,
        CancellationToken   cancellationToken = default)
    {
        // Use the paged path whenever any query param is provided (US_053/US_056 dashboard),
        // or fall back to the unfiltered path for backward compatibility.
        bool hasFilters = !string.IsNullOrWhiteSpace(provider)        ||
                          !string.IsNullOrWhiteSpace(appointmentType) ||
                          !string.IsNullOrWhiteSpace(status)          ||
                          page > 1;

        if (hasFilters || pageSize != 25)
        {
            var filters = new QueueFilterParams
            {
                Provider        = provider,
                AppointmentType = appointmentType,
                Status          = status,
                Page            = page,
                PageSize        = pageSize,
            };

            var paged = await _queueService.GetTodayQueuePagedAsync(filters, cancellationToken);

            _logger.LogInformation(
                "QueueController.GetTodayQueue (paged): provider={Provider}, appointmentType={AppointmentType}, " +
                "status={Status}, page={Page}/{TotalPages}, total={Total}.",
                provider, appointmentType, status, page,
                (int)Math.Ceiling(paged.TotalCount / (double)pageSize), paged.TotalCount);

            return Ok(paged);
        }

        var response = await _queueService.GetTodayQueueAsync(cancellationToken);

        _logger.LogInformation(
            "QueueController.GetTodayQueue: returned {Count} entries.", response.TotalCount);

        return Ok(response);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/queue/arrive
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Marks a patient as arrived, creates a QueueEntry with arrival_timestamp = UtcNow (AC-1).
    /// Returns 409 Conflict when the patient is already in waiting status (duplicate arrival).
    /// </summary>
    [HttpPost("arrive")]
    [ProducesResponseType(typeof(QueueEntryDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> MarkArrival(
        [FromBody] MarkArrivalRequest request,
        CancellationToken cancellationToken)
    {
        var staffUserId    = ResolveStaffUserId();
        var correlationId  = GetCorrelationId();

        var result = await _queueService.MarkArrivalAsync(
            request.AppointmentId, staffUserId, correlationId, cancellationToken);

        return result.Outcome switch
        {
            QueueOperationResult.Success  =>
                StatusCode(StatusCodes.Status201Created, result.Value),
            QueueOperationResult.NotFound =>
                NotFound(BuildError(StatusCodes.Status404NotFound, result.Message!)),
            QueueOperationResult.Conflict =>
                Conflict(BuildError(StatusCodes.Status409Conflict, result.Message!)),
            _ =>
                StatusCode(StatusCodes.Status500InternalServerError,
                    BuildError(500, "Unexpected error processing arrival.")),
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/queue/{queueId}/status
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Updates queue entry status. When status is 'cancelled', the appointment slot is
    /// also released for walk-ins (AC-3). Returns 422 for invalid status transitions.
    /// </summary>
    [HttpPut("{queueId:guid}/status")]
    [ProducesResponseType(typeof(QueueEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateStatus(
        Guid queueId,
        [FromBody] UpdateQueueStatusRequest request,
        CancellationToken cancellationToken)
    {
        var staffUserId   = ResolveStaffUserId();
        var correlationId = GetCorrelationId();

        var result = await _queueService.UpdateStatusAsync(
            queueId, request.Status, staffUserId, correlationId, cancellationToken);

        return result.Outcome switch
        {
            QueueOperationResult.Success =>
                Ok(result.Value),
            QueueOperationResult.NotFound =>
                NotFound(BuildError(StatusCodes.Status404NotFound, result.Message!)),
            QueueOperationResult.Conflict =>
                Conflict(BuildError(StatusCodes.Status409Conflict, result.Message!)),
            QueueOperationResult.UnprocessableStatusTransition =>
                UnprocessableEntity(BuildError(StatusCodes.Status422UnprocessableEntity, result.Message!)),
            _ =>
                StatusCode(500, BuildError(500, "Unexpected error updating status.")),
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/queue/{queueId}/override
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Overrides a no-show queue entry to arrived-late status. Requires a reason string
    /// (minimum 10 characters). Returns 422 when the current status is not no-show.
    /// All overrides are recorded in the audit log (TR-028).
    /// </summary>
    [HttpPut("{queueId:guid}/override")]
    [ProducesResponseType(typeof(QueueEntryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> OverrideNoShow(
        Guid queueId,
        [FromBody] OverrideNoShowRequest request,
        CancellationToken cancellationToken)
    {
        // Validate reason length (Validation state also enforced by [MinLength] attribute)
        if (request.Reason.Trim().Length < 10)
        {
            ModelState.AddModelError(nameof(request.Reason),
                "Reason must be at least 10 characters.");
            return ValidationProblem(ModelState);
        }

        var staffUserId   = ResolveStaffUserId();
        var correlationId = GetCorrelationId();

        var result = await _queueService.OverrideNoShowAsync(
            queueId, request.Reason.Trim(), staffUserId, correlationId, cancellationToken);

        return result.Outcome switch
        {
            QueueOperationResult.Success =>
                Ok(result.Value),
            QueueOperationResult.NotFound =>
                NotFound(BuildError(StatusCodes.Status404NotFound, result.Message!)),
            QueueOperationResult.UnprocessableStatusTransition =>
                UnprocessableEntity(BuildError(StatusCodes.Status422UnprocessableEntity, result.Message!)),
            _ =>
                StatusCode(500, BuildError(500, "Unexpected error processing override.")),
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/queue/{queueId}/priority   (US_054 AC-1, AC-4)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the priority of a queue entry to <c>urgent</c> or <c>normal</c>.
    /// The service automatically re-positions all today's active entries: urgent patients
    /// are assigned the lowest queue_position values (sorted by arrival_timestamp),
    /// followed by normal-priority patients. Writes a QueueAuditLog entry (AC-3).
    /// Returns the full sorted queue so the frontend can update its list in one round-trip.
    /// </summary>
    [HttpPut("{queueId:guid}/priority")]
    [ProducesResponseType(typeof(QueueReorderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SetPriority(
        Guid queueId,
        [FromBody] SetPriorityRequest request,
        CancellationToken cancellationToken)
    {
        var staffUserId   = ResolveStaffUserId();
        var correlationId = GetCorrelationId();

        var result = await _queueService.SetPriorityAsync(
            queueId, request.Priority, staffUserId, correlationId, cancellationToken);

        return result.Outcome switch
        {
            QueueOperationResult.Success =>
                Ok(result.Value),
            QueueOperationResult.NotFound =>
                NotFound(BuildError(StatusCodes.Status404NotFound, result.Message!)),
            QueueOperationResult.UnprocessableStatusTransition =>
                UnprocessableEntity(BuildError(StatusCodes.Status422UnprocessableEntity, result.Message!)),
            _ =>
                StatusCode(500, BuildError(500, "Unexpected error updating priority.")),
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PUT /api/queue/reorder   (US_054 AC-2, AC-3)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Moves a queue entry to a target 1-based position, shifting all other entries.
    /// Optimistic locking: if a concurrent modification is detected (EF concurrency token
    /// mismatch), returns 409 Conflict with the current queue state so the frontend can
    /// refresh and retry (US_054 edge case).
    /// Writes a QueueAuditLog entry with staff attribution and old/new positions (AC-3).
    /// Returns the full sorted queue on success.
    /// </summary>
    [HttpPut("reorder")]
    [ProducesResponseType(typeof(QueueReorderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(QueueReorderResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ReorderQueue(
        [FromBody] ReorderQueueRequest request,
        CancellationToken cancellationToken)
    {
        var staffUserId   = ResolveStaffUserId();
        var correlationId = GetCorrelationId();

        var result = await _queueService.ReorderQueueAsync(
            request.QueueId, request.NewPosition, staffUserId, correlationId, cancellationToken);

        if (result.Outcome == QueueOperationResult.Conflict)
        {
            _logger.LogWarning(
                "QueueController.ReorderQueue: concurrent conflict, correlationId={CorrelationId}.",
                correlationId);
            return Conflict(result.Value);  // return refreshed queue as 409 payload
        }

        return result.Outcome switch
        {
            QueueOperationResult.Success =>
                Ok(result.Value),
            QueueOperationResult.NotFound =>
                NotFound(BuildError(StatusCodes.Status404NotFound, result.Message!)),
            _ =>
                StatusCode(500, BuildError(500, "Unexpected error reordering queue.")),
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/queue/history   (US_056 AC-3)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns per-day aggregated queue metrics for the requested date range (US_056 AC-3).
    /// Date strings must be in yyyy-MM-dd format; startDate must be ≤ endDate; max 365-day range.
    /// Returns 200 with an empty Metrics list (not 404) when no data falls in the range.
    /// Results are served from Redis cache (TTL 5 min) for dashboard performance (NFR-004).
    /// </summary>
    [HttpGet("history")]
    [ProducesResponseType(typeof(QueueHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetQueueHistory(
        [FromQuery] string startDate,
        [FromQuery] string endDate,
        CancellationToken  cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(startDate) || string.IsNullOrWhiteSpace(endDate))
            return BadRequest(BuildError(StatusCodes.Status400BadRequest,
                "startDate and endDate are required (yyyy-MM-dd format)."));

        var response = await _queueService.GetQueueHistoryAsync(startDate, endDate, cancellationToken);

        if (response is null)
            return BadRequest(BuildError(StatusCodes.Status400BadRequest,
                "Invalid date range. Ensure startDate ≤ endDate and the range does not exceed 365 days."));

        _logger.LogInformation(
            "QueueController.GetQueueHistory: startDate={StartDate}, endDate={EndDate}, days={Days}.",
            startDate, endDate, response.Metrics.Count);

        return Ok(response);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/queue/history/export   (US_056 AC-4)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Exports queue history for the requested date range as a CSV file download (US_056 AC-4).
    /// Columns: Date, TotalEntries, PatientThroughput, NoShowCount, AvgWaitTimeMinutes.
    /// Returns 400 for invalid date inputs (same validation as GET /history).
    /// </summary>
    [HttpGet("history/export")]
    [Produces("text/csv")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ExportQueueHistory(
        [FromQuery] string startDate,
        [FromQuery] string endDate,
        CancellationToken  cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(startDate) || string.IsNullOrWhiteSpace(endDate))
            return BadRequest(BuildError(StatusCodes.Status400BadRequest,
                "startDate and endDate are required (yyyy-MM-dd format)."));

        var csv = await _queueService.ExportQueueHistoryAsCsvAsync(startDate, endDate, cancellationToken);

        if (csv is null)
            return BadRequest(BuildError(StatusCodes.Status400BadRequest,
                "Invalid date range. Ensure startDate ≤ endDate and the range does not exceed 365 days."));

        var fileName = $"queue-history_{startDate}_{endDate}.csv";

        _logger.LogInformation(
            "QueueController.ExportQueueHistory: startDate={StartDate}, endDate={EndDate}, bytes={Bytes}.",
            startDate, endDate, csv.Length);

        return File(csv, "text/csv; charset=utf-8", fileName);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the authenticated staff user ID from the JWT NameIdentifier claim.
    /// Returns <see cref="Guid.Empty"/> when the claim is absent (should never happen
    /// behind <c>[Authorize]</c>, but guards audit logging from null-deref).
    /// </summary>
    private Guid ResolveStaffUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
    }

    private string GetCorrelationId()
        => HttpContext.Items[CorrelationIdMiddleware.ItemsKey]?.ToString()
           ?? Guid.NewGuid().ToString();

    private ErrorResponse BuildError(int statusCode, string message)
        => new()
        {
            StatusCode    = statusCode,
            Message       = message,
            CorrelationId = GetCorrelationId(),
            Timestamp     = DateTimeOffset.UtcNow,
        };
}
