namespace UPACIP.Service.Idempotency;

/// <summary>
/// Persistence interface for idempotency records (US_102, AC-1).
///
/// All implementations must guarantee that <see cref="TryCreateAsync"/> is atomic —
/// two concurrent callers with the same key must not both succeed.
/// Redis <c>SET NX</c> satisfies this requirement.
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Returns the existing record for <paramref name="key"/>, or <c>null</c> if not found.
    /// </summary>
    Task<IdempotencyRecord?> GetAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Atomically creates a record if and only if no record exists for <paramref name="record.Key"/>.
    /// Returns <c>true</c> on success; <c>false</c> if the key already exists.
    /// Callers that receive <c>false</c> should call <see cref="GetAsync"/> to read the existing record.
    /// </summary>
    Task<bool> TryCreateAsync(IdempotencyRecord record, CancellationToken ct = default);

    /// <summary>
    /// Overwrites an existing record (e.g., marks it completed after the response is captured).
    /// No-op if the key no longer exists (TTL may have expired).
    /// </summary>
    Task UpdateAsync(IdempotencyRecord record, CancellationToken ct = default);

    /// <summary>
    /// Removes the record for <paramref name="key"/>.
    /// Used to clean up in-flight records when the original request fails with a non-retryable error,
    /// so the client may retry with the same key.
    /// </summary>
    Task RemoveAsync(string key, CancellationToken ct = default);
}
