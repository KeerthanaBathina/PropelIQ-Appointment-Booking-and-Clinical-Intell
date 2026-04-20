namespace UPACIP.Api.Models;

/// <summary>
/// Structured JSON error response returned for all unhandled exceptions,
/// constraint violations, and validation failures.
/// Intentionally excludes stack traces to prevent internal detail leakage (OWASP A05).
/// </summary>
public sealed record ErrorResponse
{
    public int StatusCode { get; init; }
    public string Message { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = string.Empty;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Optional machine-readable detail about the specific constraint or FK that was violated.
    /// Populated for database constraint errors (23505, 23503) and concurrency conflicts.
    /// </summary>
    public string? Detail { get; init; }

    /// <summary>
    /// Field-level validation errors keyed by property name.
    /// Populated by FluentValidation auto-validation failures (HTTP 400).
    /// </summary>
    public IDictionary<string, string[]>? ValidationErrors { get; init; }
}
