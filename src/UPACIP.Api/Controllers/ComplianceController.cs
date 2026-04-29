using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UPACIP.Api.Authorization;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;
using UPACIP.Service.Compliance;
using UPACIP.Service.Compliance.Models;
using Asp.Versioning;

namespace UPACIP.Api.Controllers;

/// <summary>
/// Admin-only endpoints for HIPAA technical safeguard verification (US_093, AC-1, NFR-041, NFR-042)
/// and HIPAA administrative safeguards — compliance policies, configurable rules, and PHI
/// migration protection (US_093, AC-2, AC-4, edge case 2).
///
/// Routes (task_001 — Technical Safeguards):
///   POST  /api/admin/compliance/verify        — Execute all technical safeguard checks.
///   GET   /api/admin/compliance/history       — View paginated verification run history.
///   GET   /api/admin/compliance/gaps          — View open/in-progress compliance gaps.
///   PATCH /api/admin/compliance/gaps/{gapId} — Update gap remediation status.
///
/// Routes (task_003 — Administrative Safeguards):
///   POST  /api/admin/compliance/policies                     — Create new policy version.
///   PATCH /api/admin/compliance/policies/{policyId}/approve  — Approve a policy.
///   GET   /api/admin/compliance/policies                     — List active policies.
///   GET   /api/admin/compliance/policies/{policyId}/history  — Policy version history.
///   GET   /api/admin/compliance/rules                        — List compliance rules.
///   POST  /api/admin/compliance/rules                        — Create / update a rule.
///   POST  /api/admin/compliance/rules/evaluate               — Evaluate all active rules.
///   POST  /api/admin/compliance/migration/pre-check          — PHI pre-migration check.
///   POST  /api/admin/compliance/migration/post-verify        — PHI post-migration verify.
///
/// Authorization (OWASP A01, NFR-011): All endpoints require the Admin role.
/// </summary>
[ApiVersion("1.0")]
[ApiController]
[Authorize(Policy = RbacPolicies.AdminOnly)]
[Route("api/admin/compliance")]
[Produces("application/json")]
public sealed class ComplianceController : ControllerBase
{
    private readonly IHipaaComplianceVerificationService _verificationService;
    private readonly ICompliancePolicyService            _policyService;
    private readonly IComplianceRuleEngine               _ruleEngine;
    private readonly IPhiMigrationGuard                  _migrationGuard;
    private readonly ApplicationDbContext                _db;
    private readonly ILogger<ComplianceController>       _logger;

    public ComplianceController(
        IHipaaComplianceVerificationService verificationService,
        ICompliancePolicyService            policyService,
        IComplianceRuleEngine               ruleEngine,
        IPhiMigrationGuard                  migrationGuard,
        ApplicationDbContext                db,
        ILogger<ComplianceController>       logger)
    {
        _verificationService = verificationService;
        _policyService       = policyService;
        _ruleEngine          = ruleEngine;
        _migrationGuard      = migrationGuard;
        _db                  = db;
        _logger              = logger;
    }

    // ── POST /api/admin/compliance/verify ────────────────────────────────────

    /// <summary>
    /// Triggers a full HIPAA technical safeguard verification run (AC-1).
    /// Executes all registered compliance checks and returns the aggregated report.
    /// Creates ComplianceGap records for any failed checks with a 30-day remediation deadline.
    /// </summary>
    [HttpPost("verify")]
    [ProducesResponseType(typeof(ComplianceVerificationReport), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RunVerification(CancellationToken ct)
    {
        var adminIdentity = ResolveAdminIdentity();

        _logger.LogInformation(
            "ComplianceController: verification triggered by admin '{Admin}'.", adminIdentity);

        var report = await _verificationService.RunVerificationAsync(adminIdentity, ct);
        return Ok(report);
    }

    // ── GET /api/admin/compliance/history ────────────────────────────────────

    /// <summary>
    /// Returns paginated HIPAA compliance verification history ordered by most recent first.
    /// </summary>
    [HttpGet("history")]
    [ProducesResponseType(typeof(ComplianceHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetHistory(
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (page     < 1) page     = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var totalCount = await _db.ComplianceVerificationLogs.CountAsync(ct);

        var logs = await _db.ComplianceVerificationLogs
            .OrderByDescending(l => l.ExecutedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(l => new ComplianceHistoryItem
            {
                Id           = l.Id,
                ExecutedAtUtc = l.ExecutedAtUtc,
                ExecutedBy   = l.ExecutedBy,
                TotalChecks  = l.TotalChecks,
                PassedChecks = l.PassedChecks,
                FailedChecks = l.FailedChecks,
                Status       = l.Status
            })
            .ToListAsync(ct);

        return Ok(new ComplianceHistoryResponse
        {
            Items      = logs,
            Page       = page,
            PageSize   = pageSize,
            TotalCount = totalCount
        });
    }

    // ── GET /api/admin/compliance/gaps ───────────────────────────────────────

    /// <summary>
    /// Returns open and in-progress compliance gaps, with optional filters.
    /// </summary>
    [HttpGet("gaps")]
    [ProducesResponseType(typeof(IReadOnlyList<ComplianceGap>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetGaps(
        [FromQuery] string? status      = null,
        [FromQuery] string? controlName = null,
        CancellationToken ct = default)
    {
        var query = _db.ComplianceGaps
            .Where(g => g.Status != "Remediated")
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(g => g.Status == status);

        if (!string.IsNullOrWhiteSpace(controlName))
            query = query.Where(g => g.ControlName.Contains(controlName));

        var gaps = await query
            .OrderByDescending(g => g.IdentifiedAtUtc)
            .ToListAsync(ct);

        return Ok(gaps);
    }

    // ── PATCH /api/admin/compliance/gaps/{gapId} ─────────────────────────────

    /// <summary>
    /// Updates a compliance gap's remediation status and optional notes.
    /// Sets RemediatedAtUtc when status transitions to "Remediated".
    /// </summary>
    [HttpPatch("gaps/{gapId:guid}")]
    [ProducesResponseType(typeof(ComplianceGap), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateGap(
        Guid gapId,
        [FromBody] UpdateGapRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Status))
            return BadRequest(new { error = "Status is required." });

        var allowedStatuses = new[] { "Open", "InProgress", "Remediated" };
        if (!allowedStatuses.Contains(request.Status))
            return BadRequest(new { error = $"Status must be one of: {string.Join(", ", allowedStatuses)}." });

        var gap = await _db.ComplianceGaps.FindAsync(new object[] { gapId }, ct);
        if (gap is null)
            return NotFound(new { error = $"Compliance gap {gapId} not found." });

        gap.Status = request.Status;

        if (request.Status == "Remediated" && gap.RemediatedAtUtc is null)
            gap.RemediatedAtUtc = DateTime.UtcNow;

        if (!string.IsNullOrWhiteSpace(request.RemediationNotes))
            gap.RemediationPlan = request.RemediationNotes;

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "ComplianceController: gap {GapId} updated to status '{Status}' by '{Admin}'.",
            gapId, request.Status, ResolveAdminIdentity());

        return Ok(gap);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string ResolveAdminIdentity()
    {
        var nameIdentifier = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var email          = User.FindFirstValue(ClaimTypes.Email);
        return email ?? nameIdentifier ?? "unknown-admin";
    }

    // ── POST /api/admin/compliance/policies ──────────────────────────────────

    /// <summary>
    /// Creates a new compliance policy version (AC-2).
    /// Assigns Version = 1 for new policies or max+1 for revisions.
    /// The new policy starts in "Draft" status and requires approval to become "Active".
    /// </summary>
    [HttpPost("policies")]
    [ProducesResponseType(typeof(CompliancePolicy), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreatePolicy(
        [FromBody] CreatePolicyRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.PolicyType) ||
            string.IsNullOrWhiteSpace(request.Title)      ||
            string.IsNullOrWhiteSpace(request.Content)    ||
            string.IsNullOrWhiteSpace(request.HipaaReference))
        {
            return BadRequest(new { error = "PolicyType, Title, Content, and HipaaReference are required." });
        }

        var allowedTypes = new[] { "SecurityPolicy", "TrainingRequirement", "IncidentResponseProcedure" };
        if (!allowedTypes.Contains(request.PolicyType))
            return BadRequest(new { error = $"PolicyType must be one of: {string.Join(", ", allowedTypes)}." });

        var policy = new CompliancePolicy
        {
            PolicyType     = request.PolicyType,
            Title          = request.Title,
            Content        = request.Content,
            HipaaReference = request.HipaaReference,
            Status         = "Draft",   // set by service
            CreatedBy      = ResolveAdminIdentity(),
            CreatedAtUtc   = DateTime.UtcNow,
            ExpirationDate = request.ExpirationDate,
        };

        var created = await _policyService.CreatePolicyAsync(policy, ct);

        return CreatedAtAction(
            nameof(GetPolicyHistory),
            new { title = created.Title },
            created);
    }

    // ── PATCH /api/admin/compliance/policies/{policyId}/approve ─────────────

    /// <summary>
    /// Approves a compliance policy draft, transitioning it to Active (AC-2).
    /// Supersedes any currently active version of the same (PolicyType + Title).
    /// </summary>
    [HttpPatch("policies/{policyId:guid}/approve")]
    [ProducesResponseType(typeof(CompliancePolicy), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ApprovePolicy(
        Guid policyId,
        [FromBody] ApprovePolicyRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ApprovedBy))
            request.ApprovedBy = ResolveAdminIdentity();

        try
        {
            var policy = await _policyService.ApprovePolicyAsync(policyId, request.ApprovedBy, ct);
            return Ok(policy);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("not found"))
        {
            return NotFound(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── GET /api/admin/compliance/policies ───────────────────────────────────

    /// <summary>
    /// Lists active compliance policies (AC-2 — HIPAA audit evidence query).
    /// Optionally filter by policyType: SecurityPolicy, TrainingRequirement, IncidentResponseProcedure.
    /// </summary>
    [HttpGet("policies")]
    [ProducesResponseType(typeof(IReadOnlyList<CompliancePolicy>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetActivePolicies(
        [FromQuery] string? policyType = null,
        CancellationToken ct = default)
    {
        var policies = await _policyService.GetActivePoliciesAsync(policyType, ct);
        return Ok(policies);
    }

    // ── GET /api/admin/compliance/policies/{title}/history ───────────────────

    /// <summary>
    /// Returns all versions of a policy by title, ordered newest-first (AC-2 audit trail).
    /// </summary>
    [HttpGet("policies/history")]
    [ProducesResponseType(typeof(IReadOnlyList<CompliancePolicy>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetPolicyHistory(
        [FromQuery] string title,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(title))
            return BadRequest(new { error = "title query parameter is required." });

        var history = await _policyService.GetPolicyHistoryAsync(title, ct);
        return Ok(history);
    }

    // ── GET /api/admin/compliance/rules ──────────────────────────────────────

    /// <summary>
    /// Lists all compliance evaluation rules (edge case 2).
    /// Supports filtering by category and isActive.
    /// </summary>
    [HttpGet("rules")]
    [ProducesResponseType(typeof(IReadOnlyList<ComplianceRule>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetRules(
        [FromQuery] string? category = null,
        [FromQuery] bool?   isActive = null,
        CancellationToken ct = default)
    {
        var query = _db.ComplianceRules.AsQueryable();

        if (!string.IsNullOrWhiteSpace(category))
            query = query.Where(r => r.Category == category);

        if (isActive.HasValue)
            query = query.Where(r => r.IsActive == isActive.Value);

        var rules = await query
            .OrderBy(r => r.Severity)
            .ThenBy(r => r.RuleName)
            .ToListAsync(ct);

        return Ok(rules);
    }

    // ── POST /api/admin/compliance/rules ─────────────────────────────────────

    /// <summary>
    /// Creates or updates a compliance rule (edge case 2 — no code deployment needed).
    /// Upserts by RuleName.
    /// </summary>
    [HttpPost("rules")]
    [ProducesResponseType(typeof(ComplianceRule), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpsertRule(
        [FromBody] UpsertRuleRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RuleName)               ||
            string.IsNullOrWhiteSpace(request.Category)               ||
            string.IsNullOrWhiteSpace(request.EvaluationCriteriaJson) ||
            string.IsNullOrWhiteSpace(request.HipaaReference))
        {
            return BadRequest(new { error = "RuleName, Category, EvaluationCriteriaJson, and HipaaReference are required." });
        }

        var rule = new ComplianceRule
        {
            RuleName               = request.RuleName,
            Category               = request.Category,
            Description            = request.Description ?? string.Empty,
            EvaluationCriteriaJson = request.EvaluationCriteriaJson,
            IsActive               = request.IsActive ?? true,
            Severity               = request.Severity ?? "High",
            HipaaReference         = request.HipaaReference,
            RemediationGuidance    = request.RemediationGuidance,
        };

        try
        {
            var upserted = await _ruleEngine.UpsertRuleAsync(rule, ct);
            return Ok(upserted);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    // ── POST /api/admin/compliance/rules/evaluate ────────────────────────────

    /// <summary>
    /// Evaluates all active compliance rules and returns results (edge case 2).
    /// </summary>
    [HttpPost("rules/evaluate")]
    [ProducesResponseType(typeof(IReadOnlyList<ComplianceRuleEvaluation>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> EvaluateRules(CancellationToken ct)
    {
        var results = await _ruleEngine.EvaluateAllRulesAsync(ct);
        return Ok(results);
    }

    // ── POST /api/admin/compliance/migration/pre-check ───────────────────────

    /// <summary>
    /// Runs PHI protection pre-migration check (AC-4, DR-031).
    /// Returns Safe=false when blocking issues are detected — migration must NOT proceed.
    /// </summary>
    [HttpPost("migration/pre-check")]
    [ProducesResponseType(typeof(PhiMigrationCheckResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> PreMigrationCheck(CancellationToken ct)
    {
        var result = await _migrationGuard.PreMigrationCheckAsync(ct);
        return Ok(result);
    }

    // ── POST /api/admin/compliance/migration/post-verify ────────────────────

    /// <summary>
    /// Runs PHI accessibility post-migration verification (AC-4, DR-031).
    /// Confirms all PHI columns remain accessible after migration completes.
    /// </summary>
    [HttpPost("migration/post-verify")]
    [ProducesResponseType(typeof(PhiMigrationCheckResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> PostMigrationVerify(CancellationToken ct)
    {
        var result = await _migrationGuard.PostMigrationVerifyAsync(ct);
        return Ok(result);
    }
}

// ── Request / response DTOs ───────────────────────────────────────────────────

/// <summary>Request body for updating a compliance gap's remediation status.</summary>
public sealed class UpdateGapRequest
{
    /// <summary>New status: "Open", "InProgress", or "Remediated".</summary>
    public required string Status { get; set; }

    /// <summary>Optional updated remediation notes or plan.</summary>
    public string? RemediationNotes { get; set; }
}

/// <summary>Paginated compliance verification history response.</summary>
public sealed class ComplianceHistoryResponse
{
    public required IReadOnlyList<ComplianceHistoryItem> Items { get; set; }
    public int Page       { get; set; }
    public int PageSize   { get; set; }
    public int TotalCount { get; set; }
}

/// <summary>Summary row for a compliance verification run history listing.</summary>
public sealed class ComplianceHistoryItem
{
    public Guid     Id            { get; set; }
    public DateTime ExecutedAtUtc { get; set; }
    public required string ExecutedBy   { get; set; }
    public int      TotalChecks   { get; set; }
    public int      PassedChecks  { get; set; }
    public int      FailedChecks  { get; set; }
    public required string Status        { get; set; }
}

/// <summary>Request body for creating a new compliance policy version.</summary>
public sealed class CreatePolicyRequest
{
    public required string PolicyType     { get; set; }
    public required string Title          { get; set; }
    public required string Content        { get; set; }
    public required string HipaaReference { get; set; }
    public DateTime?       ExpirationDate { get; set; }
}

/// <summary>Request body for approving a compliance policy.</summary>
public sealed class ApprovePolicyRequest
{
    /// <summary>Compliance officer identity; defaults to the authenticated admin if omitted.</summary>
    public string? ApprovedBy { get; set; }
}

/// <summary>Request body for creating or updating a compliance evaluation rule.</summary>
public sealed class UpsertRuleRequest
{
    public required string RuleName               { get; set; }
    public required string Category               { get; set; }
    public string?         Description            { get; set; }
    public required string EvaluationCriteriaJson { get; set; }
    public bool?           IsActive               { get; set; }
    public string?         Severity               { get; set; }
    public required string HipaaReference         { get; set; }
    public string?         RemediationGuidance    { get; set; }
}
