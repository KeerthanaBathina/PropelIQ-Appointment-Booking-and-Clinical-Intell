namespace UPACIP.Api.Attributes;

/// <summary>
/// Marks a controller action (or entire controller) as requiring idempotency enforcement
/// via <c>IdempotencyMiddleware</c> (US_102, AC-1).
///
/// Usage:
/// <code>
/// // POST endpoint — Idempotency-Key header is required
/// [HttpPost]
/// [IdempotentEndpoint]
/// public IActionResult BookAppointment(...) { ... }
///
/// // PUT endpoint — Idempotency-Key is optional (PUT is naturally idempotent)
/// [HttpPut("{id}")]
/// [IdempotentEndpoint(Required = false)]
/// public IActionResult UpdateAppointment(...) { ... }
/// </code>
///
/// When applied at the class level, all actions in the controller are subject to
/// idempotency enforcement.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class IdempotentEndpointAttribute : Attribute
{
    /// <summary>
    /// When <c>true</c> (default), requests without an <c>Idempotency-Key</c> header are
    /// rejected with HTTP 400.  Set to <c>false</c> for PUT/DELETE endpoints where the
    /// header is optional but response caching is still desired when provided.
    /// </summary>
    public bool Required { get; init; } = true;
}
