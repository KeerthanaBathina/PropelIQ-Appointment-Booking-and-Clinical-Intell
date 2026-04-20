using UPACIP.DataAccess.Entities.OwnedTypes;
using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Represents a scheduled or completed patient appointment.
/// <see cref="Version"/> is used as an EF Core optimistic-concurrency token to prevent
/// lost-update anomalies when two staff members edit the same slot simultaneously.
/// </summary>
public sealed class Appointment : BaseEntity
{
    /// <summary>FK to the owning <see cref="Patient"/>.</summary>
    public Guid PatientId { get; set; }

    /// <summary>UTC date and time the appointment is scheduled to start.</summary>
    public DateTime AppointmentTime { get; set; }

    /// <summary>Current lifecycle state of the appointment.</summary>
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Scheduled;

    /// <summary>True when the patient arrived without a prior booking.</summary>
    public bool IsWalkIn { get; set; }

    /// <summary>
    /// Patient-stated scheduling preferences serialized as JSONB.
    /// Null when the patient accepted the first available slot.
    /// </summary>
    public PreferredSlotCriteria? PreferredSlotCriteria { get; set; }

    /// <summary>
    /// Row version counter used as EF Core optimistic-concurrency token.
    /// Incremented on every update; stale-write attempts throw <c>DbUpdateConcurrencyException</c>.
    /// </summary>
    public int Version { get; set; }

    // -------------------------------------------------------------------------
    // Navigation properties
    // -------------------------------------------------------------------------

    public Patient Patient { get; set; } = null!;

    public QueueEntry? QueueEntry { get; set; }

    public ICollection<NotificationLog> Notifications { get; set; } = [];
}
