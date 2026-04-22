using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;

namespace UPACIP.Service.Notifications;

/// <summary>
/// Singleton service that buffers <see cref="NotificationLog"/> writes in memory when
/// the persistence path is unavailable, and flushes them in order when storage recovers
/// (US_037 EC-1).
///
/// Hard cap: 1000 entries.  If the buffer reaches capacity a <c>LogCritical</c> alert
/// is emitted and the excess entry is discarded to avoid unbounded memory growth.
/// </summary>
public sealed class BufferedNotificationLogWriter
{
    private readonly List<NotificationLog>              _buffer      = new();
    private readonly SemaphoreSlim                      _flushLock   = new(1, 1);
    private readonly IServiceScopeFactory               _scopeFactory;
    private readonly ILogger<BufferedNotificationLogWriter> _logger;

    private const int MaxBuffer = 1000;

    public BufferedNotificationLogWriter(
        IServiceScopeFactory                    scopeFactory,
        ILogger<BufferedNotificationLogWriter>  logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    /// <summary>
    /// Attempts to flush any pending buffered entries, then persists <paramref name="entry"/>.
    /// On persistence failure the entry is added to the buffer (up to <see cref="MaxBuffer"/>).
    /// </summary>
    public async Task WriteAsync(NotificationLog entry, CancellationToken ct = default)
    {
        // Try to drain the buffer first so entries are written in order.
        await FlushAsync(ct);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.NotificationLogs.Add(entry);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Notification log write failed for appointment {AppointmentId} " +
                "(type: {Type}, status: {Status}). Buffering entry.",
                entry.AppointmentId, entry.NotificationType, entry.Status);

            await _flushLock.WaitAsync(ct);
            try
            {
                if (_buffer.Count < MaxBuffer)
                {
                    _buffer.Add(entry);
                }
                else
                {
                    _logger.LogCritical(
                        "[ADMIN ALERT] Notification log buffer is at capacity ({Cap}). " +
                        "Entry for appointment {AppointmentId} (type: {Type}) discarded. " +
                        "Persistence must be restored immediately.",
                        MaxBuffer, entry.AppointmentId, entry.NotificationType);
                }
            }
            finally
            {
                _flushLock.Release();
            }
        }
    }

    /// <summary>
    /// Attempts to flush all buffered entries to durable storage in the order they were
    /// received.  If persistence is still unavailable the buffer is left intact for the
    /// next attempt.
    /// </summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (_buffer.Count == 0) return;

        await _flushLock.WaitAsync(ct);
        try
        {
            if (_buffer.Count == 0) return;

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.NotificationLogs.AddRange(_buffer);
            await db.SaveChangesAsync(ct);

            var flushed = _buffer.Count;
            _buffer.Clear();

            _logger.LogInformation(
                "Flushed {Count} buffered notification log entries after persistence recovery.",
                flushed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Notification log buffer flush failed — {Count} entries remain pending. " +
                "Will retry on next write cycle.",
                _buffer.Count);
            // Leave buffer intact; persistence still unavailable.
        }
        finally
        {
            _flushLock.Release();
        }
    }
}
