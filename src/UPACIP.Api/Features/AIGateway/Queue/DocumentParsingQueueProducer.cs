using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using UPACIP.Api.Features.AIGateway.Configuration;
using UPACIP.Api.Features.AIGateway.Contracts;

namespace UPACIP.Api.Features.AIGateway.Queue;

/// <summary>
/// Redis LIST-backed producer that enqueues document parsing AI requests
/// (US_067 TASK_003, AC-4).
///
/// Uses <c>RPUSH</c> (<see cref="IDatabase.ListRightPushAsync"/>) on the configured
/// queue key to maintain FIFO ordering. The consumer uses <c>LPOP</c> on the same key.
///
/// Queue depth saturation warnings are emitted when depth exceeds 80 % of
/// <see cref="QueueOptions.MaxQueueDepth"/> to enable proactive scaling.
///
/// Thread-safe: <see cref="IDatabase"/> is obtained fresh from the multiplexer on
/// each call, consistent with the StackExchange.Redis concurrency model.
/// </summary>
public sealed class DocumentParsingQueueProducer : IDocumentParsingQueueProducer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented        = false,
    };

    private readonly IConnectionMultiplexer           _redis;
    private readonly QueueOptions                     _options;
    private readonly ILogger<DocumentParsingQueueProducer> _logger;

    public DocumentParsingQueueProducer(
        IConnectionMultiplexer                redis,
        IOptions<QueueOptions>                options,
        ILogger<DocumentParsingQueueProducer> logger)
    {
        _redis   = redis;
        _options = options.Value;
        _logger  = logger;
    }

    /// <inheritdoc/>
    public async Task<QueueJobReceipt> EnqueueAsync(
        AIRequest         request,
        Guid              documentId,
        string            correlationId,
        CancellationToken cancellationToken = default)
    {
        var jobId = Guid.NewGuid();

        var message = new QueueMessage
        {
            JobId         = jobId,
            DocumentId    = documentId,
            Request       = request,
            Priority      = _options.DefaultPriority,
            EnqueuedAt    = DateTimeOffset.UtcNow,
            RetryCount    = 0,
            MaxRetries    = _options.MaxRetries,
            CorrelationId = correlationId,
        };

        var payload = JsonSerializer.Serialize(message, JsonOptions);
        var db      = _redis.GetDatabase();

        var queueDepth = await db.ListRightPushAsync(_options.QueueKey, payload);

        // Saturation warning: log when depth exceeds 80% of configured maximum.
        var saturationThreshold = (long)(_options.MaxQueueDepth * 0.8);
        if (queueDepth >= saturationThreshold)
        {
            _logger.LogWarning(
                "AI Queue: queue saturation warning. " +
                "QueueDepth={QueueDepth} SaturationThreshold={Threshold} MaxQueueDepth={Max} " +
                "CorrelationId={CorrelationId}",
                queueDepth, saturationThreshold, _options.MaxQueueDepth, correlationId);
        }

        _logger.LogInformation(
            "AI Queue: job enqueued. JobId={JobId} DocumentId={DocumentId} " +
            "QueueDepth={QueueDepth} CorrelationId={CorrelationId}",
            jobId, documentId, queueDepth, correlationId);

        return new QueueJobReceipt(jobId, queueDepth);
    }

    /// <inheritdoc/>
    public async Task<long> GetQueueDepthAsync(CancellationToken cancellationToken = default)
    {
        var db = _redis.GetDatabase();
        return await db.ListLengthAsync(_options.QueueKey);
    }
}
