using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace UPACIP.Service.Audit;

/// <summary>
/// Redis list-backed implementation of <see cref="IAuditQueueService"/> (US_064 edge case).
///
/// Queue mechanics:
///   RPUSH (enqueue) → ... items ... → LPOP (dequeue) — FIFO order.
///   List key: <c>AuditQueueSettings.RedisQueueKey</c> (default <c>audit:failover:queue</c>).
///
/// Fallback layers (defence-in-depth):
///   1. Redis RPUSH — primary failover store.
///   2. Local JSON file (<c>AuditQueueSettings.LocalFallbackPath</c>) — when Redis is also
///      unavailable.  Each entry is appended as a newline-delimited JSON record so the file
///      can be parsed line-by-line for manual recovery.
///   3. Log.Critical — when both Redis and local file write fail, the entry is at risk of
///      loss but the calling operation is NEVER disrupted (fail-open, NFR-012).
///
/// Thread-safety: all Redis operations are via <see cref="IConnectionMultiplexer"/> which is
/// thread-safe. File writes use a per-date lock obtained from a concurrent dictionary.
///
/// PII (IpAddress, UserAgent) is stored in the queue payload because it is needed to
/// reconstruct the full <c>AuditLog</c> entity on flush. It MUST NOT appear in structured
/// application logs (NFR-017); note the log lines below only emit <c>LogId</c>.
/// </summary>
public sealed class AuditQueueService : IAuditQueueService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // Per-date file locks to prevent concurrent file append races.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object>
        FileLocks = new();

    private readonly IConnectionMultiplexer       _redis;
    private readonly AuditQueueSettings           _settings;
    private readonly ILogger<AuditQueueService>   _logger;

    public AuditQueueService(
        IConnectionMultiplexer      redis,
        IOptions<AuditQueueSettings> settings,
        ILogger<AuditQueueService>  logger)
    {
        _redis    = redis;
        _settings = settings.Value;
        _logger   = logger;
    }

    /// <inheritdoc/>
    public async Task EnqueueAsync(AuditLogQueueEntry entry, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(entry, JsonOpts);

        // ── Layer 1: Redis RPUSH ─────────────────────────────────────────────────────────────
        try
        {
            var db = _redis.GetDatabase();
            await db.ListRightPushAsync(_settings.RedisQueueKey, json);

            _logger.LogWarning(
                "AuditQueueService: entry {LogId} queued to Redis failover ({Key}). EnqueuedAt={EnqueuedAt:O}.",
                entry.LogId, _settings.RedisQueueKey, entry.EnqueuedAt);
            return;
        }
        catch (Exception redisEx) when (redisEx is RedisException or RedisTimeoutException or RedisConnectionException)
        {
            _logger.LogError(redisEx,
                "AuditQueueService: Redis unavailable for entry {LogId}. Falling back to local file.",
                entry.LogId);
        }

        // ── Layer 2: Local JSON file fallback ─────────────────────────────────────────────────
        await WriteLocalFallbackAsync(json, entry.LogId, ct);
    }

    /// <inheritdoc/>
    public async Task<AuditLogQueueEntry?> DequeueAsync(CancellationToken ct = default)
    {
        try
        {
            var db  = _redis.GetDatabase();
            var raw = await db.ListLeftPopAsync(_settings.RedisQueueKey);

            if (raw.IsNullOrEmpty)
                return null;

            return JsonSerializer.Deserialize<AuditLogQueueEntry>(raw!, JsonOpts);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException or RedisConnectionException
                                       or JsonException)
        {
            _logger.LogWarning(ex, "AuditQueueService: DequeueAsync failed — {ExType}.", ex.GetType().Name);
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<long> GetQueueDepthAsync(CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            return await db.ListLengthAsync(_settings.RedisQueueKey);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException or RedisConnectionException)
        {
            _logger.LogWarning(ex, "AuditQueueService: GetQueueDepthAsync failed — Redis unavailable.");
            return -1L;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────────────────────

    private async Task WriteLocalFallbackAsync(string json, Guid logId, CancellationToken ct)
    {
        var dateSuffix = DateTime.UtcNow.ToString("yyyy-MM-dd");
        var filePath   = _settings.LocalFallbackPath.Replace("{date}", dateSuffix,
            StringComparison.OrdinalIgnoreCase);

        try
        {
            // Ensure the directory exists.
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // Lock per date-based file path to prevent concurrent append races.
            var fileLock = FileLocks.GetOrAdd(dateSuffix, _ => new object());
            lock (fileLock)
            {
                File.AppendAllText(filePath, json + Environment.NewLine, System.Text.Encoding.UTF8);
            }

            _logger.LogCritical(
                "AuditQueueService: entry {LogId} written to LOCAL FILE fallback ({Path}). " +
                "Both PostgreSQL and Redis were unavailable — manual recovery required.",
                logId, filePath);
        }
        catch (Exception fileEx)
        {
            // Both Redis and file write failed — entry is at risk of loss.
            // Log at Critical so monitoring systems alert on this condition.
            _logger.LogCritical(
                fileEx,
                "CRITICAL: Both PostgreSQL and Redis unavailable for audit logging. " +
                "Entry {LogId} at risk of loss. File fallback also failed ({Path}).",
                logId, filePath);
        }

        await Task.CompletedTask;
    }
}
