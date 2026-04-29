namespace UPACIP.Service.Idempotency;

/// <summary>
/// Persisted record for a single idempotent request (US_102, AC-1).
///
/// Lifecycle:
///   1. Created with <see cref="IsCompleted"/> = <c>false</c> (in-flight) when the first
///      request arrives — concurrent duplicates see 409 Conflict.
///   2. Updated with <see cref="IsCompleted"/> = <c>true</c>, <see cref="StatusCode"/>,
///      and <see cref="ResponseBody"/> after the downstream handler completes.
///   3. Expired automatically by Redis after <c>IdempotencyOptions.TtlHours</c>.
///
/// Body hash (edge case 1):
///   <see cref="RequestBodyHash"/> is a SHA-256 hex digest of the raw request body bytes.
///   If a second request presents the same key but a different hash, HTTP 422 is returned.
/// </summary>
public sealed class IdempotencyRecord
{
    /// <summary>Client-supplied idempotency key (UUID v4).</summary>
    public required string Key { get; init; }

    /// <summary>SHA-256 hex digest of the raw request body bytes.</summary>
    public required string RequestBodyHash { get; init; }

    /// <summary>HTTP status code of the completed response.  0 while in-flight.</summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// UTF-8 response body string.  <c>null</c> when the response exceeded
    /// <c>IdempotencyOptions.MaxBodySizeBytes</c> or the request is still in-flight.
    /// </summary>
    public string? ResponseBody { get; set; }

    /// <summary>
    /// Selected response headers captured for faithful replay.
    /// Only <c>Content-Type</c> is captured by default.
    /// </summary>
    public Dictionary<string, string> ResponseHeaders { get; set; } = new();

    /// <summary>UTC timestamp when the record was first created.</summary>
    public DateTime CreatedUtc { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// <c>false</c> while the original request is in-flight;
    /// <c>true</c> once the response has been captured and cached.
    /// </summary>
    public bool IsCompleted { get; set; }
}
