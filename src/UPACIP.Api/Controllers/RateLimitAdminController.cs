using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using UPACIP.Api.Authorization;
using UPACIP.Service.AiSafety;
using Asp.Versioning;

namespace UPACIP.Api.Controllers;

// ── Request / Response DTOs ───────────────────────────────────────────────────

/// <summary>Body for a temporary rate limit override request.</summary>
public sealed record SetOverrideRequest
{
    /// <summary>Target user identifier (GUID).</summary>
    [Required]
    public string UserId { get; init; } = string.Empty;

    /// <summary>
    /// Override limit for the sliding window.
    /// Must be between 1 and 5000 (hard cap).
    /// </summary>
    [Required, Range(1, 5000)]
    public int OverrideLimit { get; init; }

    /// <summary>
    /// How long the override remains active in minutes.
    /// Must be between 1 and 480 (8 hours).
    /// </summary>
    [Required, Range(1, 480)]
    public int DurationMinutes { get; init; }
}

/// <summary>Response body for override and status endpoints.</summary>
public sealed record RateLimitStatusResponse
{
    public string UserId           { get; init; } = string.Empty;
    public string UserRole         { get; init; } = string.Empty;
    public int    AppliedLimit     { get; init; }
    public int    RemainingRequests { get; init; }
    public int    CurrentCount     { get; init; }
    public bool   IsAllowed        { get; init; }
}

/// <summary>Response body for a successful override SET operation.</summary>
public sealed record SetOverrideResponse
{
    public string UserId         { get; init; } = string.Empty;
    public int    OverrideLimit  { get; init; }
    public int    DurationMinutes { get; init; }
    public string ExpiresAt      { get; init; } = string.Empty;
    public string SetByAdminId   { get; init; } = string.Empty;
}

// ── Controller ────────────────────────────────────────────────────────────────

/// <summary>
/// Admin-only endpoints for managing temporary AI rate limit overrides
/// (US_079 task_003, AC-4 edge case — staff bulk operations).
///
/// Routes:
///   POST   /api/admin/rate-limits/override        — Set a temporary override for a user.
///   GET    /api/admin/rate-limits/{userId}         — Get current rate limit status for a user.
///   DELETE /api/admin/rate-limits/override/{userId} — Remove a temporary override.
///
/// Authorization (OWASP A01):
///   All endpoints require the Admin role (AdminOnly policy).
///
/// Audit logging (AIR-S04, NFR-012):
///   Every write logs admin user ID, target user ID, override limit, and duration
///   at Information level. No PII or sensitive data is logged.
/// </summary>
[ApiVersion("1.0")]
[ApiController]
[Route("api/admin/rate-limits")]
[Authorize(Policy = RbacPolicies.AdminOnly)]
[Produces("application/json")]
public sealed class RateLimitAdminController : ControllerBase
{
    private readonly IAiRateLimiter                  _rateLimiter;
    private readonly ILogger<RateLimitAdminController> _logger;

    public RateLimitAdminController(
        IAiRateLimiter                    rateLimiter,
        ILogger<RateLimitAdminController> logger)
    {
        _rateLimiter = rateLimiter;
        _logger      = logger;
    }

    // ── POST /api/admin/rate-limits/override ─────────────────────────────────

    /// <summary>
    /// Sets a temporary rate limit override for the specified user.
    /// The override supersedes the user's role-based default for the configured duration,
    /// after which the role default automatically resumes (Redis TTL).
    /// </summary>
    /// <param name="request">Override parameters: target user, new limit, duration.</param>
    /// <response code="200">Override stored successfully.</response>
    /// <response code="400">Request body is invalid.</response>
    /// <response code="401">Caller is not authenticated.</response>
    /// <response code="403">Caller is not an Admin.</response>
    [HttpPost("override")]
    [ProducesResponseType(typeof(SetOverrideResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SetOverride(
        [FromBody] SetOverrideRequest request,
        CancellationToken             cancellationToken = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var adminId   = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown-admin";
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(request.DurationMinutes);

        await _rateLimiter.SetTemporaryOverrideAsync(
            request.UserId,
            request.OverrideLimit,
            request.DurationMinutes,
            cancellationToken);

        _logger.LogInformation(
            "RateLimitAdmin: override set. " +
            "AdminId={AdminId} TargetUserId={TargetUserId} " +
            "OverrideLimit={Limit} DurationMinutes={Duration} ExpiresAt={ExpiresAt}",
            adminId, request.UserId, request.OverrideLimit,
            request.DurationMinutes, expiresAt.ToString("o"));

        return Ok(new SetOverrideResponse
        {
            UserId          = request.UserId,
            OverrideLimit   = request.OverrideLimit,
            DurationMinutes = request.DurationMinutes,
            ExpiresAt       = expiresAt.ToString("o"),
            SetByAdminId    = adminId,
        });
    }

    // ── GET /api/admin/rate-limits/{userId} ───────────────────────────────────

    /// <summary>
    /// Returns the current rate limit status (quota used / remaining, applied limit)
    /// for the specified user without modifying the counter.
    /// </summary>
    /// <param name="userId">Target user identifier.</param>
    /// <param name="role">
    /// The user's role (Patient / Staff / Admin) — required to resolve the applicable limit.
    /// </param>
    /// <response code="200">Status retrieved.</response>
    /// <response code="400">userId is empty or role is invalid.</response>
    [HttpGet("{userId}")]
    [ProducesResponseType(typeof(RateLimitStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetStatus(
        [FromRoute] string userId,
        [FromQuery] string role = "Patient",
        CancellationToken  cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest(new { error = "userId is required." });

        var result = await _rateLimiter.GetRemainingQuotaAsync(userId, role, cancellationToken);

        return Ok(new RateLimitStatusResponse
        {
            UserId            = userId,
            UserRole          = role,
            AppliedLimit      = result.AppliedLimit,
            RemainingRequests = result.RemainingRequests,
            CurrentCount      = result.CurrentCount,
            IsAllowed         = result.IsAllowed,
        });
    }

    // ── DELETE /api/admin/rate-limits/override/{userId} ───────────────────────

    /// <summary>
    /// Removes a temporary rate limit override for the specified user,
    /// immediately reverting to the role-based default.
    /// </summary>
    /// <param name="userId">Target user identifier whose override should be removed.</param>
    /// <response code="204">Override removed (or no override was present — idempotent).</response>
    /// <response code="400">userId is empty.</response>
    [HttpDelete("override/{userId}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DeleteOverride(
        [FromRoute] string userId,
        CancellationToken  cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return BadRequest(new { error = "userId is required." });

        var adminId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown-admin";

        await _rateLimiter.ClearTemporaryOverrideAsync(userId);

        _logger.LogInformation(
            "RateLimitAdmin: override removed. AdminId={AdminId} TargetUserId={TargetUserId}",
            adminId, userId);

        return NoContent();
    }
}
