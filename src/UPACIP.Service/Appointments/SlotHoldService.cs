using Microsoft.Extensions.Logging;
using UPACIP.Service.Caching;

namespace UPACIP.Service.Appointments;

/// <summary>
/// Redis-based implementation of <see cref="ISlotHoldService"/> (US_018, AC-3).
///
/// Uses <see cref="ICacheService"/> which wraps <c>IDistributedCache</c> with:
///   - Polly circuit breaker (3 failures → 30 s open window)
///   - Graceful fallback on Redis unavailability (cache errors never break the pipeline)
///
/// Atomicity: AcquireHoldAsync uses a get-then-conditional-set pattern.
/// This is intentionally non-atomic because <c>ICacheService</c> does not expose SET NX.
/// The small race window between GET and SET is acceptable: the DB-level uniqueness check
/// and EF Core Version concurrency token are the hard concurrency guarantees (FR-012, TR-015).
/// Redis holds are a UX convenience that reduces 409 conflict frequency under normal load.
/// </summary>
public sealed class SlotHoldService : ISlotHoldService
{
    private static readonly TimeSpan HoldTtl = TimeSpan.FromSeconds(60); // AC-3

    private readonly ICacheService _cache;
    private readonly ILogger<SlotHoldService> _logger;

    public SlotHoldService(ICacheService cache, ILogger<SlotHoldService> logger)
    {
        _cache  = cache;
        _logger = logger;
    }

    // Redis key for a given slot hold.
    private static string HoldKey(string slotId)              => $"hold:{slotId}";
    private static string Normalise(string email)              => email.ToLowerInvariant();

    /// <inheritdoc/>
    public async Task<bool> AcquireHoldAsync(
        string            slotId,
        string            userEmail,
        CancellationToken cancellationToken = default)
    {
        var key         = HoldKey(slotId);
        var normEmail   = Normalise(userEmail);
        var existing    = await _cache.GetAsync<string>(key, cancellationToken);

        // Deny if a different user already holds the slot.
        if (existing is not null && existing != normEmail)
        {
            _logger.LogDebug(
                "Slot hold denied: slot={SlotId}, requested by {UserEmail} but already held by another user.",
                slotId, userEmail);
            return false;
        }

        // Grant hold (or refresh TTL for the same user).
        await _cache.SetAsync(key, normEmail, HoldTtl, cancellationToken);

        _logger.LogInformation(
            "Slot hold acquired: slot={SlotId}, user={UserEmail}, ttlSeconds=60.",
            slotId, userEmail);
        return true;
    }

    /// <inheritdoc/>
    public async Task ReleaseHoldAsync(
        string            slotId,
        string            userEmail,
        CancellationToken cancellationToken = default)
    {
        var key       = HoldKey(slotId);
        var normEmail = Normalise(userEmail);
        var existing  = await _cache.GetAsync<string>(key, cancellationToken);

        if (existing != normEmail)
        {
            // No hold, or held by someone else — nothing to release.
            _logger.LogDebug(
                "Slot hold release skipped: slot={SlotId}, user={UserEmail} does not own the hold.",
                slotId, userEmail);
            return;
        }

        await _cache.RemoveAsync(key, cancellationToken);

        _logger.LogInformation(
            "Slot hold released: slot={SlotId}, user={UserEmail}.",
            slotId, userEmail);
    }

    /// <inheritdoc/>
    public async Task<bool> IsHeldByUserAsync(
        string            slotId,
        string            userEmail,
        CancellationToken cancellationToken = default)
    {
        var key       = HoldKey(slotId);
        var normEmail = Normalise(userEmail);
        var existing  = await _cache.GetAsync<string>(key, cancellationToken);
        return existing == normEmail;
    }
}
