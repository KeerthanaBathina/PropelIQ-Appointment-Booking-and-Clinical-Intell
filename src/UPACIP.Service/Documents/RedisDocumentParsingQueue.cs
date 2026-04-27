using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace UPACIP.Service.Documents;

/// <summary>
/// Redis LIST-backed implementation of <see cref="IDocumentParsingQueue"/> using the same
/// queue key as <see cref="DocumentParsingQueueService"/> and <see cref="DocumentParsingDispatcher"/>
/// (US_071 TASK_003, AC-3).
///
/// <para>
/// All methods are soft-fail: <see cref="RedisException"/> and
/// <see cref="RedisTimeoutException"/> are caught, a warning is logged, and a safe default
/// is returned so queue monitoring never disrupts normal API request handling (AC-4).
/// </para>
///
/// <para>
/// Singleton lifetime — the underlying <see cref="IConnectionMultiplexer"/> is a singleton
/// and <see cref="IDatabase"/> instances are cheap to obtain from it per-call (consistent
/// with the StackExchange.Redis concurrency model).
/// </para>
/// </summary>
public sealed class RedisDocumentParsingQueue : IDocumentParsingQueue
{
    // ─────────────────────────────────────────────────────────────────────────
    // Constants
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Redis list key — kept in sync with <see cref="DocumentParsingQueueService.QueueKey"/>.</summary>
    private const string QueueKey = DocumentParsingQueueService.QueueKey;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented               = false,
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Fields
    // ─────────────────────────────────────────────────────────────────────────

    private readonly IConnectionMultiplexer           _redis;
    private readonly ILogger<RedisDocumentParsingQueue> _logger;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    public RedisDocumentParsingQueue(
        IConnectionMultiplexer             redis,
        ILogger<RedisDocumentParsingQueue> logger)
    {
        _redis  = redis;
        _logger = logger;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IDocumentParsingQueue
    // ─────────────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task EnqueueAsync(DocumentParsingQueueJob job, CancellationToken ct = default)
    {
        try
        {
            var payload = JsonSerializer.Serialize(job, JsonOptions);
            var db      = _redis.GetDatabase();
            await db.ListRightPushAsync(QueueKey, payload);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            _logger.LogWarning(ex,
                "RedisDocumentParsingQueue: Redis unavailable during EnqueueAsync. " +
                "DocumentId={DocumentId}", job.DocumentId);
        }
    }

    /// <inheritdoc/>
    public async Task<DocumentParsingQueueJob?> DequeueAsync(CancellationToken ct = default)
    {
        try
        {
            var db  = _redis.GetDatabase();
            var raw = await db.ListLeftPopAsync(QueueKey);
            if (raw.IsNullOrEmpty) return null;

            return JsonSerializer.Deserialize<DocumentParsingQueueJob>(raw!, JsonOptions);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            _logger.LogWarning(ex,
                "RedisDocumentParsingQueue: Redis unavailable during DequeueAsync.");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task ReenqueueFrontAsync(DocumentParsingQueueJob job, CancellationToken ct = default)
    {
        try
        {
            var payload = JsonSerializer.Serialize(job, JsonOptions);
            var db      = _redis.GetDatabase();
            // LPUSH places the item at the head so it is the NEXT item processed (FIFO restoration).
            await db.ListLeftPushAsync(QueueKey, payload);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            _logger.LogWarning(ex,
                "RedisDocumentParsingQueue: Redis unavailable during ReenqueueFrontAsync. " +
                "DocumentId={DocumentId}", job.DocumentId);
        }
    }

    /// <inheritdoc/>
    public async Task<long> GetDepthAsync(CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            return await db.ListLengthAsync(QueueKey);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            _logger.LogWarning(ex,
                "RedisDocumentParsingQueue: Redis unavailable during GetDepthAsync. Returning 0.");
            return 0L;
        }
    }

    /// <inheritdoc/>
    public async Task<TimeSpan?> GetOldestItemAgeAsync(CancellationToken ct = default)
    {
        try
        {
            var db  = _redis.GetDatabase();
            // LINDEX 0 returns the head element without removing it (O(1) for head).
            var raw = await db.ListGetByIndexAsync(QueueKey, 0);
            if (raw.IsNullOrEmpty) return null;

            var job = JsonSerializer.Deserialize<DocumentParsingQueueJob>(raw!, JsonOptions);
            if (job is null) return null;

            return DateTimeOffset.UtcNow - job.EnqueuedAt;
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            _logger.LogWarning(ex,
                "RedisDocumentParsingQueue: Redis unavailable during GetOldestItemAgeAsync. Returning null.");
            return null;
        }
    }
}
