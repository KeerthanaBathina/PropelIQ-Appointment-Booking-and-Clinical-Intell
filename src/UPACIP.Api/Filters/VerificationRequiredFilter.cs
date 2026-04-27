using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using UPACIP.Service.Verification;

namespace UPACIP.Api.Filters;

/// <summary>
/// Action filter that intercepts finalization attempts on AI-generated records that have
/// not yet been verified by a staff member (US_075, AC-4, AIR-S02, AIR-S03).
///
/// <para>
/// Apply via <c>[ServiceFilter(typeof(VerificationRequiredFilter))]</c> on any controller
/// action that finalizes a <c>MedicalCode</c> or <c>ExtractedData</c> record — for example,
/// billing submission, patient profile consolidation, or any workflow that reads
/// AI-generated clinical data as authoritative input.
/// </para>
///
/// <para>
/// The filter reads a <c>recordId</c> and <c>recordType</c> value from the current route
/// or action arguments, delegates the status check to
/// <see cref="IVerificationEnforcementService.IsVerifiedAsync"/>, and returns
/// <c>400 Bad Request</c> with a structured <c>verification_required</c> error body when
/// the record is still in pending status.
/// </para>
///
/// <para>
/// <b>No auto-approval bypass is possible via this filter.</b>  The check is enforced for
/// every request regardless of caller role or origin, per AIR-S03 compliance.
/// </para>
///
/// <para>
/// Route parameter names checked (in order):
/// <list type="bullet">
///   <item><c>id</c> — interpreted as <c>recordId</c> (standard REST convention).</item>
///   <item><c>recordId</c> — explicit override.</item>
/// </list>
/// The <c>recordType</c> is read from the route data key <c>recordType</c>; if absent it
/// defaults to <c>"MedicalCode"</c> to protect code-finalization endpoints.
/// </para>
///
/// <para>Registered as Scoped in the DI container — resolved per request by
/// <c>ServiceFilterAttribute</c>.</para>
/// </summary>
public sealed class VerificationRequiredFilter : IAsyncActionFilter
{
    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly IVerificationEnforcementService _enforcementService;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public VerificationRequiredFilter(IVerificationEnforcementService enforcementService)
    {
        _enforcementService = enforcementService;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IAsyncActionFilter
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        // ── Resolve recordId from route or action arguments ───────────────────
        Guid   recordId   = Guid.Empty;
        string recordType = VerificationEnforcementService.RecordTypeMedicalCode;

        if (context.RouteData.Values.TryGetValue("id", out var idObj)
            && Guid.TryParse(idObj?.ToString(), out var idFromRoute))
        {
            recordId = idFromRoute;
        }
        else if (context.RouteData.Values.TryGetValue("recordId", out var ridObj)
                 && Guid.TryParse(ridObj?.ToString(), out var ridFromRoute))
        {
            recordId = ridFromRoute;
        }
        else if (context.ActionArguments.TryGetValue("id", out var idArg)
                 && Guid.TryParse(idArg?.ToString(), out var idFromArg))
        {
            recordId = idFromArg;
        }

        if (context.RouteData.Values.TryGetValue("recordType", out var rtObj)
            && rtObj is string rtString && !string.IsNullOrWhiteSpace(rtString))
        {
            recordType = rtString;
        }

        // ── If we couldn't resolve a recordId, pass through ───────────────────
        // The action itself will handle the 404 / validation error.
        if (recordId == Guid.Empty)
        {
            await next();
            return;
        }

        // ── Enforce verification gate (AC-4) ──────────────────────────────────
        bool isVerified = await _enforcementService.IsVerifiedAsync(
            recordId, recordType, context.HttpContext.RequestAborted);

        if (!isVerified)
        {
            context.Result = new BadRequestObjectResult(new
            {
                error   = "verification_required",
                message = "Human verification is required before this operation can be completed. " +
                          "No AI-generated clinical data may be finalized without staff approval.",
                recordId,
                recordType,
            });
            return;
        }

        // ── Verification confirmed — proceed to the action ───────────────────
        await next();
    }
}
