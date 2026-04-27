using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using UPACIP.Api.Middleware;
using UPACIP.Api.Models;

namespace UPACIP.Api.Filters;

/// <summary>
/// Global action filter that intercepts invalid model state before the controller action
/// executes and returns a structured <see cref="ValidationErrorResponse"/> (US_066 AC-4,
/// FR-095, OWASP A05).
///
/// Registered as a global filter in Program.cs:
/// <code>builder.Services.AddControllers(o =&gt; o.Filters.Add&lt;ValidateModelAttribute&gt;())</code>
///
/// <c>ApiBehaviorOptions.SuppressModelStateInvalidFilter = true</c> is also configured in
/// Program.cs so that the <c>[ApiController]</c> built-in automatic 400 response is
/// suppressed. This filter takes sole ownership of model state validation responses,
/// ensuring all 400 responses share the <see cref="ValidationErrorResponse"/> shape.
///
/// Error messages include the field name and a user-friendly message only — no stack
/// traces, no internal exception details, no database schema hints (OWASP A05, NFR-017).
/// FluentValidation validators already produce user-friendly messages that meet AC-4.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class ValidateModelAttribute : ActionFilterAttribute
{
    /// <inheritdoc />
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid) return;

        var correlationId = context.HttpContext.Items[CorrelationIdMiddleware.ItemsKey]?.ToString()
                            ?? Guid.NewGuid().ToString();

        var errors = context.ModelState
            .Where(kvp => kvp.Value?.Errors.Count > 0)
            .SelectMany(kvp => kvp.Value!.Errors.Select(e => new ValidationFieldError
            {
                Field = kvp.Key,
                // Use the FluentValidation / DataAnnotation message when available;
                // fall back to a generic field name message if empty (AC-4 compliance).
                Message = string.IsNullOrWhiteSpace(e.ErrorMessage)
                    ? $"Field '{kvp.Key}' is invalid. Check the expected format and retry."
                    : e.ErrorMessage,
            }))
            .ToList();

        var response = new ValidationErrorResponse
        {
            CorrelationId = correlationId,
            Errors        = errors,
        };

        context.Result = new BadRequestObjectResult(response);
    }
}
