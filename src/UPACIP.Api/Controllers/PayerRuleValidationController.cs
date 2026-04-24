using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPACIP.Api.Authorization;
using UPACIP.Api.Middleware;
using UPACIP.Api.Models;
using UPACIP.Service.Coding;

namespace UPACIP.Api.Controllers;

/// <summary>
/// REST endpoints for payer rule validation, multi-code assignment, bundling rule checks,
/// and conflict resolution (US_051, AC-1, AC-2, AC-3, AC-4, FR-066).
///
/// Routes:
///   GET  /api/coding/payer-rules/{patientId}   — Validate patient codes against payer rules (AC-1, AC-2).
///   POST /api/coding/multi-assign              — Assign multiple codes with individual verification (AC-3).
///   POST /api/coding/validate-bundling         — Check code set for NCCI bundling violations (AC-4).
///   POST /api/coding/resolve-conflict          — Record staff resolution for a payer rule conflict (edge case).
///
/// Authorization (OWASP A01 — Broken Access Control):
///   All endpoints require the <c>Staff</c> or <c>Admin</c> role.
///   The acting user's ID is always read from the authenticated JWT claims; it is never
///   accepted from the request body.
///
/// Caching (NFR-030):
///   Payer rule sets are cached in Redis with a 5-minute TTL by the service layer.
///   Cache invalidation occurs on payer rule updates via the service.
///
/// Idempotency (NFR-034):
///   POST /api/coding/multi-assign accepts an optional <c>idempotency_key</c> header.
///   Duplicate requests with the same key within a 24-hour window receive the original result.
/// </summary>
[ApiController]
[Authorize(Policy = RbacPolicies.StaffOrAdmin)]
[Produces("application/json")]
public sealed class PayerRuleValidationController : ControllerBase
{
    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly IPayerRuleValidationService             _validationService;
    private readonly IMultiCodeAssignmentService             _assignmentService;
    private readonly ILogger<PayerRuleValidationController>  _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public PayerRuleValidationController(
        IPayerRuleValidationService            validationService,
        IMultiCodeAssignmentService            assignmentService,
        ILogger<PayerRuleValidationController> logger)
    {
        _validationService = validationService;
        _assignmentService = assignmentService;
        _logger            = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GET /api/coding/payer-rules/{patientId}
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates a patient's current code set against payer-specific rules and returns
    /// all detected violations, denial risks, and bundling issues (US_051 AC-1, AC-2).
    ///
    /// When payer-specific rules are unavailable for the given payer, CMS-default rules
    /// are applied and <c>is_cms_default = true</c> is returned (EC-1).
    /// </summary>
    /// <param name="patientId">Target patient primary key.</param>
    /// <param name="payerId">
    /// Optional payer identifier (e.g. <c>BCBS-IL</c>).
    /// When omitted CMS-default rules are applied.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet("api/coding/payer-rules/{patientId:guid}")]
    [ProducesResponseType(typeof(PayerValidationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPayerValidationAsync(
        [FromRoute] Guid    patientId,
        [FromQuery] string? payerId,
        CancellationToken   ct)
    {
        // Sanitise payer ID — reject values that look like injection attempts (NFR-018)
        if (payerId is not null && payerId.Length > 50)
            return BadRequest(BuildError(400, "payer_id must not exceed 50 characters."));

        var svcResult = await _validationService.ValidateCodeCombinationsAsync(patientId, payerId, ct);
        return Ok(MapToResponse(svcResult));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/coding/multi-assign
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Assigns multiple ICD-10 and CPT codes to a patient encounter with individual
    /// code verification and billing priority ordering (US_051 AC-3).
    ///
    /// Idempotent when the same <c>idempotency_key</c> is supplied within 24 hours (NFR-034).
    /// </summary>
    /// <param name="request">Multi-code assignment request body.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("api/coding/multi-assign")]
    [ProducesResponseType(typeof(MultiCodeAssignmentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> AssignMultipleCodesAsync(
        [FromBody] MultiCodeAssignmentRequest request,
        CancellationToken                     ct)
    {
        if (!ModelState.IsValid)
            return UnprocessableEntity(BuildValidationError());

        var actingUserId = GetCurrentUserId();
        if (actingUserId is null)
            return Unauthorized(BuildError(401, "Unable to identify authenticated user."));

        var items  = request.Codes.Select((c, i) => new CodeAssignmentItem
        {
            CodeValue     = c.CodeValue,
            CodeType      = c.CodeType,
            Description   = c.Description,
            Justification = c.Justification,
            SequenceOrder = c.SequenceOrder > 0 ? c.SequenceOrder : i + 1,
        }).ToList();

        var svcResult = await _assignmentService.AssignMultipleCodesAsync(
            request.PatientId, items, actingUserId.Value, ct);

        return StatusCode(StatusCodes.Status201Created, MapToResponse(svcResult));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/coding/validate-bundling
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Validates a set of CPT code values against NCCI bundling edits (US_051 AC-4).
    /// Returns the list of violated code pairs with modifier suggestions.
    /// An empty violations list means the code set passed all bundling checks.
    /// </summary>
    /// <param name="request">Bundling validation request body.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("api/coding/validate-bundling")]
    [ProducesResponseType(typeof(BundlingValidationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ValidateBundlingAsync(
        [FromBody] BundlingValidationRequest request,
        CancellationToken                    ct)
    {
        if (!ModelState.IsValid)
            return UnprocessableEntity(BuildValidationError());

        var svcViolations = await _validationService.ValidateBundlingRulesAsync(request.CodeValues, ct);

        return Ok(new BundlingValidationResponse
        {
            PatientId  = request.PatientId,
            Passed     = svcViolations.Count == 0,
            Violations = svcViolations.Select(MapToDto).ToList(),
        });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // POST /api/coding/resolve-conflict
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Records a staff decision when a payer rule conflicts with clinical documentation
    /// (US_051 edge case).  Populates the resolution fields on the violation record and
    /// writes an audit entry.
    /// </summary>
    /// <param name="request">Conflict resolution request body.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpPost("api/coding/resolve-conflict")]
    [ProducesResponseType(typeof(ConflictResolutionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResolveConflictAsync(
        [FromBody] ConflictResolutionRequest request,
        CancellationToken                    ct)
    {
        if (!ModelState.IsValid)
            return UnprocessableEntity(BuildValidationError());

        var actingUserId = GetCurrentUserId();
        if (actingUserId is null)
            return Unauthorized(BuildError(401, "Unable to identify authenticated user."));

        try
        {
            var record = new ConflictResolutionRecord
            {
                ViolationId    = request.ViolationId,
                PatientId      = request.PatientId,
                ResolutionType = request.ResolutionType,
                Justification  = request.Justification,
                SelectedCode   = request.SelectedCode,
            };

            var svcResult = await _validationService.RecordConflictResolutionAsync(
                record, actingUserId.Value, ct);

            return Ok(new ConflictResolutionResponse
            {
                ViolationId      = svcResult.ViolationId,
                ResolutionStatus = svcResult.ResolutionStatus,
                ResolvedAt       = svcResult.ResolvedAt,
            });
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(
                "PayerRuleValidationController.ResolveConflict: {Message}", ex.Message);
            return NotFound(BuildError(404, ex.Message));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private Guid? GetCurrentUserId()
    {
        var raw = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
               ?? User.FindFirst("sub")?.Value;
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Service → API DTO mapping helpers
    // ─────────────────────────────────────────────────────────────────────────

    private static PayerValidationResponse MapToResponse(PayerValidationRunResult r) => new()
    {
        PayerName          = r.PayerName,
        IsCmsDefault       = r.IsCmsDefault,
        ValidationResults  = r.Violations.Select(MapToDto).ToList(),
        DenialRisks        = r.DenialRisks.Select(MapToDto).ToList(),
        BundlingViolations = r.BundlingViolations.Select(MapToDto).ToList(),
    };

    private static MultiCodeAssignmentResponse MapToResponse(MultiCodeAssignmentRunResult r) => new()
    {
        PatientId     = r.PatientId,
        AssignedCodes = r.AssignedCodes.Select(c => new AssignedCodeDto
        {
            CodeId                = c.CodeId,
            CodeValue             = c.CodeValue,
            CodeType              = c.CodeType.ToString(),
            Description           = c.Description,
            SequenceOrder         = c.SequenceOrder,
            PayerValidationStatus = c.PayerValidationStatus.ToString(),
        }).ToList(),
    };

    private static PayerValidationResultDto MapToDto(PayerViolationItem v) => new()
    {
        ViolationId       = v.ViolationId,
        RuleId            = v.RuleId,
        Severity          = v.Severity.ToString(),
        Description       = v.Description,
        DenialReason      = v.DenialReason,
        AffectedCodes     = v.AffectedCodes,
        CorrectiveActions = v.CorrectiveActions.Select(MapToDto).ToList(),
        IsCmsDefault      = v.IsCmsDefault,
    };

    private static ClaimDenialRiskDto MapToDto(DenialRiskItem d) => new()
    {
        RiskLevel            = d.RiskLevel,
        CodePair             = d.CodePair,
        DenialReason         = d.DenialReason,
        HistoricalDenialRate = d.HistoricalDenialRate,
        CorrectiveActions    = d.CorrectiveActions.Select(MapToDto).ToList(),
    };

    private static BundlingRuleResultDto MapToDto(BundlingViolationItem b) => new()
    {
        Column1Code         = b.Column1Code,
        Column2Code         = b.Column2Code,
        EditType            = b.EditType.ToString(),
        ApplicableModifiers = b.ApplicableModifiers,
        Description         = b.Description,
    };

    private static CorrectiveActionDto MapToDto(CorrectiveItem c) => new()
    {
        ActionType    = c.ActionType,
        Description   = c.Description,
        SuggestedCode = c.SuggestedCode,
    };

    private string GetCorrelationId()
        => HttpContext.Items[CorrelationIdMiddleware.ItemsKey]?.ToString()
           ?? Guid.NewGuid().ToString();

    private ErrorResponse BuildError(int statusCode, string message) => new()
    {
        StatusCode    = statusCode,
        Message       = message,
        CorrelationId = GetCorrelationId(),
        Timestamp     = DateTimeOffset.UtcNow,
    };

    private ErrorResponse BuildValidationError() => new()
    {
        StatusCode       = 422,
        Message          = "One or more validation errors occurred.",
        CorrelationId    = GetCorrelationId(),
        Timestamp        = DateTimeOffset.UtcNow,
        ValidationErrors = ModelState.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value?.Errors.Select(e => e.ErrorMessage).ToArray() ?? []),
    };
}
