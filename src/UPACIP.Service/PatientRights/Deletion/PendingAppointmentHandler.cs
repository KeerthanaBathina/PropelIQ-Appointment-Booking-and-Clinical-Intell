using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Enums;

namespace UPACIP.Service.PatientRights.Deletion;

/// <summary>
/// Cancels all pending (Scheduled) appointments for a patient before deletion proceeds
/// (US_094, AC-3, edge case 1).
///
/// Staff notification is handled by the existing appointment cancellation notification
/// workflow — the status change to <see cref="AppointmentStatus.Cancelled"/> triggers
/// the standard downstream notification pipeline.
/// </summary>
public sealed class PendingAppointmentHandler
{
    private readonly ApplicationDbContext               _db;
    private readonly ILogger<PendingAppointmentHandler> _logger;

    public PendingAppointmentHandler(
        ApplicationDbContext               db,
        ILogger<PendingAppointmentHandler> logger)
    {
        _db     = db;
        _logger = logger;
    }

    /// <summary>
    /// Cancels all <see cref="AppointmentStatus.Scheduled"/> appointments for the patient
    /// and returns the count of cancelled records.
    /// </summary>
    public async Task<int> CancelPendingAppointmentsAsync(Guid patientId, CancellationToken ct)
    {
        var appointments = await _db.Appointments
            .Where(a => a.PatientId == patientId && a.Status == AppointmentStatus.Scheduled)
            .ToListAsync(ct);

        if (appointments.Count == 0)
            return 0;

        var now = DateTime.UtcNow;
        foreach (var appt in appointments)
        {
            appt.Status    = AppointmentStatus.Cancelled;
            appt.UpdatedAt = now;

            _logger.LogInformation(
                "DELETION_APPOINTMENT_CANCELLED: AppointmentId={AppointmentId}, PatientId={PatientId}",
                appt.Id, patientId);
        }

        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "PendingAppointmentHandler: cancelled {Count} appointments for patient {PatientId}.",
            appointments.Count, patientId);

        return appointments.Count;
    }
}
