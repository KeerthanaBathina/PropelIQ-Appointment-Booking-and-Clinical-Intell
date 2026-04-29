namespace UPACIP.Contracts.Models;

/// <summary>
/// Standard API response envelope used by all UPACIP controller endpoints (AC-1, TR-009).
///
/// Every controller action returns <c>ApiResponse&lt;T&gt;</c> to ensure a consistent
/// response shape across the Presentation layer. The <see cref="Success"/> flag lets
/// clients distinguish between successful responses and application-level errors without
/// relying solely on HTTP status codes.
///
/// Usage in controllers:
/// <code>
/// return Ok(ApiResponse&lt;PatientDto&gt;.Ok(patient));
/// return BadRequest(ApiResponse&lt;PatientDto&gt;.Fail("Validation failed", errors));
/// </code>
/// </summary>
/// <typeparam name="T">The payload type returned on success.</typeparam>
public sealed class ApiResponse<T>
{
    /// <summary>Response payload. Null when <see cref="Success"/> is false.</summary>
    public T? Data { get; init; }

    /// <summary>True when the request was processed successfully.</summary>
    public bool Success { get; init; }

    /// <summary>Human-readable summary message for the response.</summary>
    public string? Message { get; init; }

    /// <summary>List of validation or application errors. Null when <see cref="Success"/> is true.</summary>
    public List<string>? Errors { get; init; }

    /// <summary>Optional key–value metadata (e.g., pagination info, correlation ID).</summary>
    public Dictionary<string, object>? Metadata { get; init; }

    // ── Factory helpers ───────────────────────────────────────────────────────

    /// <summary>Creates a successful response containing <paramref name="data"/>.</summary>
    public static ApiResponse<T> Ok(T data, string? message = null) =>
        new() { Success = true, Data = data, Message = message };

    /// <summary>Creates a failed response with the supplied error messages.</summary>
    public static ApiResponse<T> Fail(string message, IEnumerable<string>? errors = null) =>
        new() { Success = false, Message = message, Errors = errors?.ToList() };
}
