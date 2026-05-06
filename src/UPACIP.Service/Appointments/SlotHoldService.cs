using System.Collections.Concurrent;
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
/// When Redis is unavailable, an in-memory <see cref="ConcurrentDictionary{TKey,TValue}"/>
/// acts as a fallback so that slot holds still work in development without Redis.
/// The in-memory fallback enforces the same 60-second TTL.
///
/// Atomicity: AcquireHoldAsync uses a get-then-conditional-set pattern.
/// The DB-level uniqueness check and EF Core Version concurrency token are the hard
/// concurrency guarantees (FR-012, TR-015). Redis/memory holds are a UX convenience.
/// </summary>
public sealed class SlotHoldService : ISlotHoldService
{
    private static readonly TimeSpan HoldTtl = TimeSpan.FromSeconds(60); // AC-3

    // In-memory fallback: key = slotId, value = (normalisedEmail, expiresAt).
    // Static so the dictionary survives across Scoped service instances (one per HTTP request).
    private static readonly ConcurrentDictionary<string, (string Email, DateTime ExpiresAt)> _memoryHolds = new();

    private readonly ICacheService _cache;
    private readonly ILogger<SlotHoldService> _logger;

    public SlotHoldService(ICacheService cache, ILogger<SlotHoldService> logger)
    {
        _cache  = cache;
        _logger = logger;
    }

    // Redis key for a given slot hold.
    private static string HoldKey(string slotId)              => $"hold:{slotId}";
    private static string Normalise(string email) => email.ToLowerInvariant();

    // Evict stale in-memory entries to prevent unbounded growth.
    private void EvictExpired()
    {
        var now = DateTime.UtcNow;
        foreach (var kvp in _memoryHolds)
        {
            if (kvp.Value.ExpiresAt <= now)
                _memoryHolds.TryRemove(kvp.Key, out _);
        }
    }

    /// <inheritdoc/>
    public async Task<bool> AcquireHoldAsync(
        string            slotId,
        string            userEmail,
        CancellationToken cancellationToken = default)
    {
        var key       = HoldKey(slotId);
        var normEmail = Normalise(userEmail);

        // ── Try Redis first ────────────────────────────────────────────────
        var existing = await _cache.GetAsync<string>(key, cancellationToken);

        if (existing is not null)
        {
            // Redis is available and has a value — deny if a different user holds it.
            if (existing != normEmail)
            {
                _logger.LogDebug(
                    "Slot hold denied (Redis): slot={SlotId}, requested by {UserEmail} but already held.",
                    slotId, userEmail);
                return false;
            }
        }
        else
        {
            // Redis returned null — may be unavailable. Check in-memory fallback.
            EvictExpired();
            if (_memoryHolds.TryGetValue(slotId, out var mem) && mem.ExpiresAt > DateTime.UtcNow)
            {
                if (mem.Email != normEmail)
                {
                    _logger.LogDebug(
                        "Slot hold denied (memory): slot={SlotId}, requested by {UserEmail} but already held.",
                        slotId, userEmail);
                    return false;
                }
            }
        }

        // Grant hold — write to Redis (best effort) and memory fallback.
        await _cache.SetAsync(key, normEmail, HoldTtl, cancellationToken);
        _memoryHolds[slotId] = (normEmail, DateTime.UtcNow.Add(HoldTtl));

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

        await _cache.RemoveAsync(key, cancellationToken);
        _memoryHolds.TryRemove(slotId, out _);

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

        // Try Redis first.
        var existing = await _cache.GetAsync<string>(key, cancellationToken);
        if (existing is not null)
            return existing == normEmail;

        // Redis unavailable — check in-memory fallback.
        EvictExpired();
        if (_memoryHolds.TryGetValue(slotId, out var mem) && mem.ExpiresAt > DateTime.UtcNow)
            return mem.Email == normEmail;

        return false;
    }
}
