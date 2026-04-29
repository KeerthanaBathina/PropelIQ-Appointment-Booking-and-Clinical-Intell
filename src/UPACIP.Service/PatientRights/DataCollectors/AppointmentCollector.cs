using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.Service.PatientRights.Models;

namespace UPACIP.Service.PatientRights.DataCollectors;

/// <summary>
/// Collects all appointments for a patient including nested queue entries and notification
/// history (US_094, AC-1).
/// </summary>
public sealed class AppointmentCollector
{
    private readonly ApplicationDbContext          _db;
    private readonly ILogger<AppointmentCollector> _logger;

    public AppointmentCollector(
        ApplicationDbContext          db,
        ILogger<AppointmentCollector> logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <summary>
    /// Collects all appointments for the given patient.  Returns an empty list when the
    /// patient has no appointments — this is a valid scenario (AC-1).
    /// </summary>
    public async Task<List<AppointmentData>> CollectAsync(Guid patientId, CancellationToken ct)
    {
        var appointments = await _db.Appointments
            .AsNoTracking()
            .Where(a => a.PatientId == patientId)
            .Include(a => a.QueueEntry)
            .Include(a => a.Notifications)
            .OrderBy(a => a.AppointmentTime)
            .ToListAsync(ct);

        var result = appointments.Select(a => new AppointmentData
        {
            AppointmentId   = a.Id,
            BookingReference = a.BookingReference,
            AppointmentTime = a.AppointmentTime,
            Status          = a.Status.ToString(),
            IsWalkIn        = a.IsWalkIn,
            ProviderName    = a.ProviderName,
            AppointmentType = a.AppointmentType,
            CreatedAt       = a.CreatedAt,
            QueueEntries    = a.QueueEntry is null ? new() : new()
            {
                new QueueEntryData
                {
                    QueueEntryId     = a.QueueEntry.Id,
                    ArrivalTimestamp = a.QueueEntry.ArrivalTimestamp,
                    WaitTimeMinutes  = a.QueueEntry.WaitTimeMinutes,
                    Priority         = a.QueueEntry.Priority.ToString(),
                    Status           = a.QueueEntry.Status.ToString(),
                }
            },
            NotificationLogs = a.Notifications.Select(n => new NotificationLogData
            {
                NotificationId   = n.NotificationId,
                NotificationType = n.NotificationType.ToString(),
                DeliveryChannel  = n.DeliveryChannel.ToString(),
                Status           = n.Status.ToString(),
                SentAt           = n.SentAt,
                RetryCount       = n.RetryCount,
            }).ToList(),
        }).ToList();

        _logger.LogDebug(
            "AppointmentCollector: collected {Count} appointments for patient {PatientId}.",
            result.Count,
            patientId);

        return result;
    }
}
