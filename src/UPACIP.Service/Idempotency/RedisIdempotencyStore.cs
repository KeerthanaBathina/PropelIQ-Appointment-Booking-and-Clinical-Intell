using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace UPACIP.Service.Idempotency;

/// <summary>
/// Redis-backed implementation of <see cref="IIdempotencyStore"/> (US_102, AC-1).
///
/// Storage strategy:
///   Keys are stored as JSON strings under the prefix <c>idempotency:</c>.
///   <see cref="TryCreateAsync"/> uses Redis <c>SET NX</c> (atomic set-if-not-exists) to
///   guarantee that only one caller succeeds when concurrent duplicates arrive simultaneously.
///   Every key carries a TTL equal to <c>IdempotencyOptions.TtlHours</c> so Redis reclaims
///   memory automatically without a separate purge job.
///
/// Fail behavior:
///   Redis exceptions propagate to the middleware, which removes the in-flight record on
///   catch so the client may retry.  The middleware logs and re-throws for the global
///   exception handler to surface a 500.
/// </summary>
public sealed class RedisIdempotencyStore : IIdempotencyStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IdempotencyOptions _options;
    private readonly ILogger<RedisIdempotencyStore> _logger;
    private const string KeyPrefix = "idempotency:";

    public RedisIdempotencyStore(
        IConnectionMultiplexer redis,
        IOptions<IdempotencyOptions> options,
        ILogger<RedisIdempotencyStore> logger)
    {
        _redis = redis;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IdempotencyRecord?> GetAsync(string key, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var value = await db.StringGetAsync(BuildRedisKey(key));

        if (value.IsNullOrEmpty)
            return null;

        return JsonSerializer.Deserialize<IdempotencyRecord>(value!);
    }

    /// <inheritdoc />
    public async Task<bool> TryCreateAsync(IdempotencyRecord record, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var serialized = JsonSerializer.Serialize(record);
        var ttl = TimeSpan.FromHours(_options.TtlHours);

        // SET NX — atomic: only sets the key if it does not already exist.
        var created = await db.StringSetAsync(
            BuildRedisKey(record.Key),
            serialized,
            ttl,
            When.NotExists);

        if (created)
        {
            _logger.LogDebug(
                "IDEMPOTENCY_KEY_CREATED: Key={Key}, TTL={TtlHours}h",
                record.Key, _options.TtlHours);
        }

        return created;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(IdempotencyRecord record, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        var serialized = JsonSerializer.Serialize(record);
        var ttl = TimeSpan.FromHours(_options.TtlHours);

        // When.Exists — prevents re-creating an expired/deleted key during the response phase.
        await db.StringSetAsync(
            BuildRedisKey(record.Key),
            serialized,
            ttl,
            When.Exists);

        _logger.LogDebug(
            "IDEMPOTENCY_KEY_UPDATED: Key={Key}, StatusCode={StatusCode}, Completed={IsCompleted}",
            record.Key, record.StatusCode, record.IsCompleted);
    }

    /// <inheritdoc />
    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        var db = _redis.GetDatabase();
        await db.KeyDeleteAsync(BuildRedisKey(key));

        _logger.LogDebug("IDEMPOTENCY_KEY_REMOVED: Key={Key}", key);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string BuildRedisKey(string key) => $"{KeyPrefix}{key}";
}
