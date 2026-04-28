using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using StackExchange.Redis;
using UPACIP.Service.Auth;
using UPACIP.Service.Notifications;
using UPACIP.Service.Resilience;
using UPACIP.Service.Resilience.Models;

// Disambiguate ProviderCircuitState from Polly.CircuitBreaker.CircuitState
using ProviderCircuitState = UPACIP.Service.Resilience.Models.ProviderCircuitState;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Background service that processes messages dequeued from the Redis-backed
/// SMS and email retry queues (US_084 task_001, AC-2 / AC-3).
///
/// <para>
/// Runs on a 60-second <see cref="PeriodicTimer"/>.  On each tick it:
/// <list type="number">
///   <item>Checks whether the respective circuit is still <see cref="CircuitState.Open"/>.
///     If open, skips the queue to avoid hammering the down provider.</item>
///   <item>Pops up to 10 items from the Redis list (<c>LPOP</c>).</item>
///   <item>Delivers each item directly through the concrete transport class
///     (<see cref="TwilioSmsTransport"/> / <see cref="SmtpEmailService"/>),
///     <em>not</em> through the resilient decorators, to prevent re-queuing loops.</item>
///   <item>On <see cref="BrokenCircuitException"/>: re-pushes the item to the tail of the
///     queue (circuit tripped again mid-batch).</item>
///   <item>On other transient failures: increments retry count and re-queues up to
///     <c>MaxRetryAttempts</c> (5 by default).  Discards permanently-failed items with
///     a structured error log.</item>
/// </list>
/// </para>
/// </summary>
public sealed class NotificationRetryService : BackgroundService
{
    private const string SmsRetryQueue   = "notification:sms:retry_queue";
    private const string EmailRetryQueue = "notification:email:retry_queue";
    private const int    BatchSize       = 10;
    private const int    MaxRetryAttempts = 5;

    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(60);

    private readonly IExternalServiceResilienceProvider _resilienceProvider;
    private readonly IConnectionMultiplexer              _redis;
    private readonly TwilioSmsTransport                  _smsTransport;
    private readonly SmtpEmailService                    _emailService;
    private readonly ILogger<NotificationRetryService>   _logger;

    public NotificationRetryService(
        IExternalServiceResilienceProvider  resilienceProvider,
        IConnectionMultiplexer              connectionMultiplexer,
        TwilioSmsTransport                  smsTransport,
        SmtpEmailService                    emailService,
        ILogger<NotificationRetryService>   logger,
        IOptions<ExternalServiceResilienceOptions> options)    // kept for future config access
    {
        _resilienceProvider = resilienceProvider;
        _redis              = connectionMultiplexer;
        _smsTransport       = smsTransport;
        _emailService       = emailService;
        _logger             = logger;
    }

    // ── BackgroundService ─────────────────────────────────────────────────────

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NotificationRetryService started.");

        using var timer = new PeriodicTimer(PollingInterval);

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ProcessSmsQueueAsync(stoppingToken);
                await ProcessEmailQueueAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex,
                    "Unhandled error in NotificationRetryService tick. " +
                    "Will retry on next interval.");
            }
        }

        _logger.LogInformation("NotificationRetryService stopped.");
    }

    // ── SMS retry ─────────────────────────────────────────────────────────────

    private async Task ProcessSmsQueueAsync(CancellationToken ct)
    {
        if (_resilienceProvider.GetCircuitState("Sms") == ProviderCircuitState.Open)
        {
            _logger.LogDebug(
                "SMS circuit is OPEN — skipping SMS retry queue this tick.");
            return;
        }

        var db = _redis.GetDatabase();

        for (int i = 0; i < BatchSize; i++)
        {
            var raw = await db.ListLeftPopAsync(SmsRetryQueue);
            if (!raw.HasValue) break;

            SmsRetryQueueItem? item = null;
            try
            {
                item = JsonSerializer.Deserialize<SmsRetryQueueItem>(raw!);
                if (item is null) continue;

                var message = new SmsTransportMessage(item.ToPhoneNumber, item.Body);
                await _smsTransport.SendAsync(message, ct);

                _logger.LogInformation(
                    "SMS retry succeeded for {MaskedRecipient} (attempt {Attempt}).",
                    MaskPhone(item.ToPhoneNumber),
                    item.RetryCount + 1);
            }
            catch (BrokenCircuitException)
            {
                // Circuit tripped again during the batch — re-enqueue and stop processing.
                if (item is not null)
                    await db.ListRightPushAsync(SmsRetryQueue, raw!);

                _logger.LogWarning(
                    "SMS circuit re-opened during retry batch — stopping SMS retry tick.");
                break;
            }
            catch (Exception ex)
            {
                if (item is null) continue;

                var newCount = item.RetryCount + 1;
                if (newCount >= MaxRetryAttempts)
                {
                    _logger.LogError(ex,
                        "SMS retry for {MaskedRecipient} permanently failed after " +
                        "{MaxAttempts} attempts — discarding.",
                        MaskPhone(item.ToPhoneNumber),
                        MaxRetryAttempts);
                }
                else
                {
                    var updated = item with { RetryCount = newCount };
                    await db.ListRightPushAsync(
                        SmsRetryQueue,
                        JsonSerializer.Serialize(updated));

                    _logger.LogWarning(ex,
                        "SMS retry {Attempt}/{Max} failed for {MaskedRecipient} — re-queued.",
                        newCount,
                        MaxRetryAttempts,
                        MaskPhone(item.ToPhoneNumber));
                }
            }
        }
    }

    // ── Email retry ───────────────────────────────────────────────────────────

    private async Task ProcessEmailQueueAsync(CancellationToken ct)
    {
        if (_resilienceProvider.GetCircuitState("Email") == ProviderCircuitState.Open)
        {
            _logger.LogDebug(
                "Email circuit is OPEN — skipping email retry queue this tick.");
            return;
        }

        var db = _redis.GetDatabase();

        for (int i = 0; i < BatchSize; i++)
        {
            var raw = await db.ListLeftPopAsync(EmailRetryQueue);
            if (!raw.HasValue) break;

            NotificationQueueItem? item = null;
            try
            {
                item = JsonSerializer.Deserialize<NotificationQueueItem>(raw!);
                if (item is null) continue;

                await DispatchEmailAsync(item, ct);

                _logger.LogInformation(
                    "Email retry succeeded for {MaskedRecipient} type={Type} (attempt {Attempt}).",
                    MaskEmail(item.Recipient),
                    item.Type,
                    item.RetryCount + 1);
            }
            catch (BrokenCircuitException)
            {
                if (item is not null)
                    await db.ListRightPushAsync(EmailRetryQueue, raw!);

                _logger.LogWarning(
                    "Email circuit re-opened during retry batch — stopping email retry tick.");
                break;
            }
            catch (Exception ex)
            {
                if (item is null) continue;

                var newCount = item.RetryCount + 1;
                if (newCount >= MaxRetryAttempts)
                {
                    _logger.LogError(ex,
                        "Email retry for {MaskedRecipient} type={Type} permanently failed " +
                        "after {MaxAttempts} attempts — discarding.",
                        MaskEmail(item.Recipient),
                        item.Type,
                        MaxRetryAttempts);
                }
                else
                {
                    var updated = item with { RetryCount = newCount };
                    await db.ListRightPushAsync(
                        EmailRetryQueue,
                        JsonSerializer.Serialize(updated));

                    _logger.LogWarning(ex,
                        "Email retry {Attempt}/{Max} failed for {MaskedRecipient} " +
                        "type={Type} — re-queued.",
                        newCount,
                        MaxRetryAttempts,
                        MaskEmail(item.Recipient),
                        item.Type);
                }
            }
        }
    }

    private async Task DispatchEmailAsync(NotificationQueueItem item, CancellationToken ct)
    {
        switch (item.Type)
        {
            case "Verification":
                await _emailService.SendVerificationEmailAsync(
                    item.Recipient, item.RecipientName, item.Param1 ?? string.Empty, ct);
                break;

            case "PasswordReset":
                await _emailService.SendPasswordResetEmailAsync(
                    item.Recipient, item.RecipientName, item.Param1 ?? string.Empty, ct);
                break;

            case "WaitlistOffer":
                await _emailService.SendWaitlistOfferEmailAsync(
                    item.Recipient, item.RecipientName,
                    item.Param1 ?? string.Empty,
                    item.Param2 ?? string.Empty,
                    item.BoolParam ?? false, ct);
                break;

            case "SwapCompleted":
                await _emailService.SendSwapCompletedEmailAsync(
                    item.Recipient, item.RecipientName,
                    item.Param1 ?? string.Empty,
                    item.Param2 ?? string.Empty,
                    item.Param3 ?? string.Empty, ct);
                break;

            case "ManualSwapConfirmation":
                await _emailService.SendManualSwapConfirmationEmailAsync(
                    item.Recipient, item.RecipientName,
                    item.Param1 ?? string.Empty,
                    item.Param2 ?? string.Empty, ct);
                break;

            default:
                _logger.LogWarning(
                    "Unknown email queue item type '{Type}' for {Recipient} — discarding.",
                    item.Type,
                    MaskEmail(item.Recipient));
                break;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string MaskPhone(string number)
        => number.Length > 4 ? $"***{number[^4..]}" : "****";

    private static string MaskEmail(string email)
    {
        var idx = email.IndexOf('@', StringComparison.Ordinal);
        return idx > 1 ? $"{email[0]}***{email[idx..]}" : "***@***";
    }
}
