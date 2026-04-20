using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Tracks a patient's position in the waiting-room queue for a given appointment.
/// Each appointment has at most one associated queue entry (one-to-one relationship).
/// </summary>
public sealed class QueueEntry : BaseEntity
{
    /// <summary>FK to the associated <see cref="Appointment"/> (one-to-one).</summary>
    public Guid AppointmentId { get; set; }

    /// <summary>UTC timestamp when the patient checked in and joined the queue.</summary>
    public DateTime ArrivalTimestamp { get; set; }

    /// <summary>Calculated or estimated wait time in minutes at time of last update.</summary>
    public int WaitTimeMinutes { get; set; }

    /// <summary>Queue priority — Urgent patients are moved ahead of Normal-priority entries.</summary>
    public QueuePriority Priority { get; set; } = QueuePriority.Normal;

    /// <summary>Current status of this queue entry.</summary>
    public QueueStatus Status { get; set; } = QueueStatus.Waiting;

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    public Appointment Appointment { get; set; } = null!;
}
