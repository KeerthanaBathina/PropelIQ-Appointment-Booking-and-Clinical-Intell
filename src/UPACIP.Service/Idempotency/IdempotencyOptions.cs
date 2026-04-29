namespace UPACIP.Service.Idempotency;

/// <summary>
/// Configurable options for the idempotency subsystem (US_102, AC-1).
/// Bound from the <c>Idempotency</c> section in <c>appsettings.json</c>.
/// </summary>
public sealed class IdempotencyOptions
{
    /// <summary>Configuration section name used for <c>IOptions&lt;T&gt;</c> binding.</summary>
    public const string SectionName = "Idempotency";

    /// <summary>
    /// HTTP header name that clients use to supply the idempotency key.
    /// Default: <c>Idempotency-Key</c> (IETF draft standard).
    /// </summary>
    public string HeaderName { get; init; } = "Idempotency-Key";

    /// <summary>
    /// How long idempotency records are retained in Redis before automatic expiry.
    /// Default: 24 hours — sufficient for client retry windows without excessive Redis memory use.
    /// </summary>
    public int TtlHours { get; init; } = 24;

    /// <summary>
    /// Maximum response body size in bytes that will be cached.
    /// Responses larger than this are not replayed (the state change still happens only once).
    /// Default: 1 048 576 bytes (1 MiB).
    /// </summary>
    public int MaxBodySizeBytes { get; init; } = 1_048_576;
}
