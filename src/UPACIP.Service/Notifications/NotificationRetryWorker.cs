using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Singleton <see cref="BackgroundService"/> that executes orchestration-level notification
/// retries at 1-minute, 5-minute, and 15-minute backoff intervals (US_037 AC-2).
///
/// Also implements <see cref="INotificationRetryQueue"/> so scoped services can enqueue
/// retry requests without a direct dependency on the hosted worker instance.
///
/// On each 1-minute tick the worker:
/// <list type="number">
///   <item>Flushes any buffered <see cref="BufferedNotificationLogWriter"/> entries.</item>
///   <item>Drains in-memory pending retries, separates due items from not-yet-due items.</item>
///   <item>Re-queues not-yet-due items for future ticks.</item>
///   <item>
///     For each due item, creates a fresh DI scope, re-builds the channel request with
///     the correct <c>OrchestrationAttemptNumber</c>, and invokes the channel service.
///     The channel service internally calls <see cref="INotificationDeliveryReliabilityService"/>
///     which schedules the next retry or marks the notification permanently failed.
///   </item>
/// </list>
///
/// This service never throws from <c>ExecuteAsync</c>; all per-item failures are caught
/// and logged so a single bad retry cannot stall the entire worker loop.
/// </summary>
public sealed class NotificationRetryWorker : BackgroundService, INotificationRetryQueue
{
    private readonly ConcurrentQueue<NotificationRetryRequest>  _queue = new();
    private readonly BufferedNotificationLogWriter               _buffer;
    private readonly IServiceScopeFactory                        _scopeFactory;
    private readonly ILogger<NotificationRetryWorker>            _logger;

    public NotificationRetryWorker(
        BufferedNotificationLogWriter    buffer,
        IServiceScopeFactory             scopeFactory,
        ILogger<NotificationRetryWorker> logger)
    {
        _buffer       = buffer;
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    /// <inheritdoc/>
    public void EnqueueRetry(NotificationRetryRequest request) => _queue.Enqueue(request);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("NotificationRetryWorker started.");

        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));

        while (!stoppingToken.IsCancellationRequested &&
               await timer.WaitForNextTickAsync(stoppingToken))
        {
            await _buffer.FlushAsync(stoppingToken);
            await ProcessDueRetriesAsync(stoppingToken);
        }

        _logger.LogInformation("NotificationRetryWorker stopped.");
    }

    private async Task ProcessDueRetriesAsync(CancellationToken ct)
    {
        var now     = DateTime.UtcNow;
        var due     = new List<NotificationRetryRequest>();
        var pending = new List<NotificationRetryRequest>();

        // Drain the entire queue and classify items.
        while (_queue.TryDequeue(out var item))
        {
            if (item.NextRetryAt <= now)
                due.Add(item);
            else
                pending.Add(item);
        }

        // Re-queue not-yet-due items.
        foreach (var item in pending) _queue.Enqueue(item);

        if (due.Count == 0) return;

        _logger.LogInformation(
            "NotificationRetryWorker processing {Count} due retry item(s).",
            due.Count);

        foreach (var retry in due)
        {
            try
            {
                await DispatchRetryAsync(retry, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unhandled error processing retry for appointment {AppointmentId} " +
                    "(channel: {Channel}, attempt: {Attempt}). Correlation: {CorrelationId}",
                    retry.AppointmentId, retry.Channel, retry.AttemptNumber,
                    retry.CorrelationId ?? "N/A");
            }
        }
    }

    private async Task DispatchRetryAsync(NotificationRetryRequest retry, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();

        if (retry.Channel == DeliveryChannel.Email)
        {
            var emailService = scope.ServiceProvider
                .GetRequiredService<INotificationEmailService>();

            var emailRequest = new NotificationEmailRequest(
                AppointmentId:              retry.AppointmentId,
                PatientId:                  retry.PatientId,
                PatientEmail:               retry.PatientEmail,
                PatientName:                retry.PatientName,
                AppointmentTime:            retry.AppointmentTime,
                ProviderName:               retry.ProviderName,
                AppointmentType:            retry.AppointmentType,
                NotificationType:           retry.NotificationType,
                BookingReference:           retry.BookingReference,
                CancellationLink:           retry.CancellationLink,
                CorrelationId:              retry.CorrelationId,
                OrchestrationAttemptNumber: retry.AttemptNumber);

            await emailService.SendAsync(emailRequest, ct);
        }
        else if (retry.Channel == DeliveryChannel.Sms)
        {
            var smsService = scope.ServiceProvider
                .GetRequiredService<INotificationSmsService>();

            var smsRequest = new NotificationSmsRequest(
                AppointmentId:              retry.AppointmentId,
                PatientId:                  retry.PatientId,
                PatientPhoneNumber:         retry.PatientPhoneNumber,
                PatientName:                retry.PatientName,
                AppointmentTime:            retry.AppointmentTime,
                ProviderName:               retry.ProviderName,
                AppointmentType:            retry.AppointmentType,
                NotificationType:           retry.NotificationType,
                BookingReference:           retry.BookingReference,
                CorrelationId:              retry.CorrelationId,
                OrchestrationAttemptNumber: retry.AttemptNumber);

            await smsService.SendAsync(smsRequest, ct);
        }
        else
        {
            _logger.LogWarning(
                "Unknown delivery channel '{Channel}' for retry of appointment {AppointmentId}. " +
                "Retry discarded. Correlation: {CorrelationId}",
                retry.Channel, retry.AppointmentId, retry.CorrelationId ?? "N/A");
        }
    }
}
