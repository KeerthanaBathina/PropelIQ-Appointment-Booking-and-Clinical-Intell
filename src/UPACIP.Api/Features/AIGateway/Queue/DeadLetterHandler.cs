using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using UPACIP.Api.Features.AIGateway.Configuration;

namespace UPACIP.Api.Features.AIGateway.Queue;

/// <summary>
/// Moves permanently failed queue jobs to the Redis dead-letter queue
/// (US_067 TASK_003, AC-4 edge case).
///
/// A job is routed here when <see cref="QueueMessage.IsExhausted"/> is true — meaning
/// the consumer has retried the job up to <see cref="QueueOptions.MaxRetries"/> times
/// and all attempts have failed.
///
/// Dead-letter messages are enriched with the failure reason and timestamp, then pushed
/// to a separate Redis LIST (<see cref="QueueOptions.DeadLetterKey"/>). This preserves
/// the failed payload for operational investigation without blocking the main queue.
///
/// A structured Warning log is emitted for every dead-letter event so that monitoring
/// systems (Serilog → Seq) can alert on accumulation of dead-letter entries.
/// </summary>
public sealed class DeadLetterHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented        = false,
    };

    private readonly IConnectionMultiplexer _redis;
    private readonly QueueOptions           _options;
    private readonly ILogger<DeadLetterHandler> _logger;

    public DeadLetterHandler(
        IConnectionMultiplexer      redis,
        IOptions<QueueOptions>      options,
        ILogger<DeadLetterHandler>  logger)
    {
        _redis   = redis;
        _options = options.Value;
        _logger  = logger;
    }

    /// <summary>
    /// Enriches <paramref name="message"/> with failure context and pushes it to the
    /// dead-letter Redis LIST using <c>RPUSH</c>.
    /// </summary>
    /// <param name="message">The exhausted job message.</param>
    /// <param name="failureReason">Human-readable description of the last failure.</param>
    /// <param name="cancellationToken">Used to honour shutdown signals during the push.</param>
    public async Task HandleAsync(
        QueueMessage      message,
        string            failureReason,
        CancellationToken cancellationToken = default)
    {
        var deadLetterEntry = new DeadLetterEntry
        {
            JobId         = message.JobId,
            DocumentId    = message.DocumentId,
            OriginalMessage = message,
            FailureReason = failureReason,
            FailedAt      = DateTimeOffset.UtcNow,
            RetryCount    = message.RetryCount,
            CorrelationId = message.CorrelationId,
        };

        var payload = JsonSerializer.Serialize(deadLetterEntry, JsonOptions);
        var db      = _redis.GetDatabase();

        await db.ListRightPushAsync(_options.DeadLetterKey, payload);

        var deadLetterDepth = await db.ListLengthAsync(_options.DeadLetterKey);

        _logger.LogWarning(
            "AI Queue: job moved to dead-letter queue. " +
            "JobId={JobId} DocumentId={DocumentId} RetryCount={RetryCount} " +
            "DeadLetterDepth={Depth} FailureReason={Reason} CorrelationId={CorrelationId}",
            message.JobId,
            message.DocumentId,
            message.RetryCount,
            deadLetterDepth,
            failureReason,
            message.CorrelationId);
    }

    /// <summary>Returns the current depth of the dead-letter queue.</summary>
    public async Task<long> GetDepthAsync(CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        return await db.ListLengthAsync(_options.DeadLetterKey);
    }

    // ── Private DTO ───────────────────────────────────────────────────────────

    private sealed class DeadLetterEntry
    {
        public Guid          JobId           { get; init; }
        public Guid          DocumentId      { get; init; }
        public QueueMessage  OriginalMessage { get; init; } = null!;
        public string        FailureReason   { get; init; } = string.Empty;
        public DateTimeOffset FailedAt        { get; init; }
        public int           RetryCount      { get; init; }
        public string        CorrelationId   { get; init; } = string.Empty;
    }
}
