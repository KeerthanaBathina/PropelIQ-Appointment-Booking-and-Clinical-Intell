namespace UPACIP.Api.Models;

/// <summary>
/// Structured validation error response returned by
/// <see cref="UPACIP.Api.Filters.ValidateModelAttribute"/> when model state is invalid.
///
/// Each entry in <see cref="Errors"/> identifies the failing field by name and provides
/// a user-friendly message describing the expected format — no stack traces, no internal
/// implementation details, no database schema information (US_066 AC-4, OWASP A05,
/// NFR-017).
///
/// Shape:
/// <code>
/// {
///   "correlationId": "3fa85f64-...",
///   "errors": [
///     { "field": "Email",     "message": "Field 'Email' must be a valid email address." },
///     { "field": "BirthDate", "message": "Field 'BirthDate' must be a date in YYYY-MM-DD format." }
///   ]
/// }
/// </code>
/// </summary>
public sealed record ValidationErrorResponse
{
    /// <summary>Correlation ID propagated from the incoming request for distributed tracing.</summary>
    public string CorrelationId { get; init; } = string.Empty;

    /// <summary>One entry per invalid field.</summary>
    public IReadOnlyList<ValidationFieldError> Errors { get; init; } = Array.Empty<ValidationFieldError>();
}

/// <summary>A single field-level validation failure detail.</summary>
public sealed record ValidationFieldError
{
    /// <summary>
    /// The name of the field that failed validation.
    /// Uses the same casing as the incoming JSON property (FluentValidation preserves this).
    /// </summary>
    public string Field { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable description of what the field requires.
    /// Never exposes internal logic, SQL query structure, or stack traces.
    /// </summary>
    public string Message { get; init; } = string.Empty;
}
