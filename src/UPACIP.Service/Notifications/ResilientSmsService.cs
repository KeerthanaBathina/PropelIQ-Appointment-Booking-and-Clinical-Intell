using System.Text.Json;
using Microsoft.Extensions.Logging;
using Polly.CircuitBreaker;
using StackExchange.Redis;
using UPACIP.Service.Resilience;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Polly V8 circuit-breaker decorator for <see cref="TwilioSmsTransport"/>
/// (US_084 task_001, AC-2).
///
/// <para>
/// Wraps every <see cref="SendAsync"/> call with the <c>Sms</c> resilience pipeline
/// from <see cref="IExternalServiceResilienceProvider"/>.  When the circuit is open
/// (i.e. Twilio is unavailable), the message is serialised to JSON and pushed to
/// Redis list <c>notification:sms:retry_queue</c> so it can be retried later by
/// <see cref="NotificationRetryService"/>.
/// </para>
///
/// <para>
/// This class is registered as the primary <c>ISmsTransport</c> in DI.
/// <see cref="TwilioSmsTransport"/> is also registered as its concrete type so
/// that <see cref="NotificationRetryService"/> can inject it directly (avoiding
/// re-queuing loops when retrying).
/// </para>
/// </summary>
public sealed class ResilientSmsService : ISmsTransport
{
    private const string RetryQueue = "notification:sms:retry_queue";

    private readonly TwilioSmsTransport            _inner;
    private readonly IExternalServiceResilienceProvider _resilienceProvider;
    private readonly IConnectionMultiplexer         _redis;
    private readonly ILogger<ResilientSmsService>   _logger;

    public ResilientSmsService(
        TwilioSmsTransport              inner,
        IExternalServiceResilienceProvider resilienceProvider,
        IConnectionMultiplexer          connectionMultiplexer,
        ILogger<ResilientSmsService>    logger)
    {
        _inner              = inner;
        _resilienceProvider = resilienceProvider;
        _redis              = connectionMultiplexer;
        _logger             = logger;
    }

    /// <inheritdoc />
    public async Task<SmsDeliveryAttemptResult> SendAsync(
        SmsTransportMessage message,
        CancellationToken   cancellationToken = default)
    {
        var pipeline = _resilienceProvider.GetPipeline("Sms");

        try
        {
            return await pipeline.ExecuteAsync(
                static async (state, ct) => await state.inner.SendAsync(state.message, ct),
                (inner: _inner, message),
                cancellationToken);
        }
        catch (BrokenCircuitException ex)
        {
            _logger.LogWarning(ex,
                "SMS circuit is OPEN — queuing message to {RetryQueue} for later re-delivery. " +
                "Recipient: {MaskedRecipient}",
                RetryQueue,
                MaskPhoneNumber(message.ToPhoneNumber));

            await EnqueueForRetryAsync(message, cancellationToken);
            return SmsDeliveryAttemptResult.Failed(0, "SMS circuit open — queued for retry.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SMS send failed after resilience pipeline exhausted retries. " +
                "Recipient: {MaskedRecipient}",
                MaskPhoneNumber(message.ToPhoneNumber));
            throw;
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task EnqueueForRetryAsync(SmsTransportMessage message, CancellationToken ct)
    {
        try
        {
            var item = new SmsRetryQueueItem(
                message.ToPhoneNumber,
                message.Body,
                RetryCount: 0,
                QueuedAt: DateTime.UtcNow);

            var json = JsonSerializer.Serialize(item);
            var db   = _redis.GetDatabase();
            await db.ListRightPushAsync(RetryQueue, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to enqueue SMS retry item for {MaskedRecipient}. " +
                "Message will be lost.",
                MaskPhoneNumber(message.ToPhoneNumber));
        }
    }

    private static string MaskPhoneNumber(string number)
        => number.Length > 4 ? $"***{number[^4..]}" : "****";
}

/// <summary>
/// JSON-serialisable envelope for an SMS message queued for later retry
/// (US_084 task_001, AC-2).
/// </summary>
public sealed record SmsRetryQueueItem(
    string   ToPhoneNumber,
    string   Body,
    int      RetryCount,
    DateTime QueuedAt);
