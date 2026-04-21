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

    /// <summary>
    /// Unique booking reference generated on creation (AC-4, US_018).
    /// Format: BK-{YYYYMMDD}-{6-char-uppercase-alphanumeric} (e.g. BK-20260421-X7R2KP).
    /// Null for walk-in appointments and appointments created before US_018 was deployed.
    /// Max 20 characters: "BK-" (3) + "YYYYMMDD" (8) + "-" (1) + 6-char suffix (6) + 2 spare = 20.
    /// </summary>
    public string? BookingReference { get; set; }

    /// <summary>UTC date and time the appointment is scheduled to start.</summary>
    public DateTime AppointmentTime { get; set; }

    /// <summary>Current lifecycle state of the appointment.</summary>
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Scheduled;

    /// <summary>True when the patient arrived without a prior booking.</summary>
    public bool IsWalkIn { get; set; }

    // -------------------------------------------------------------------------
    // Provider / type fields (US_017)
    // -------------------------------------------------------------------------

    /// <summary>Identifier of the provider assigned to this appointment. Null for walk-ins or legacy records.</summary>
    public Guid? ProviderId { get; set; }

    /// <summary>Full display name of the assigned provider (max 100). Denormalised for fast display.</summary>
    public string? ProviderName { get; set; }

    /// <summary>Category of the appointment (e.g. "General Checkup", "Follow-up"). Max 50 chars.</summary>
    public string? AppointmentType { get; set; }

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

    // -------------------------------------------------------------------------
    // No-show risk metadata (US_026, AIR-006, FR-014)
    // Persisted after each risk calculation so staff views and slot-swap
    // prioritization never require a real-time re-score on every list render.
    // All fields are nullable: existing appointments have no score until their
    // first recalculation and the migration rolls them forward safely.
    // -------------------------------------------------------------------------

    /// <summary>
    /// Persisted no-show risk score in range [0, 100] (enforced by DB CHECK constraint).
    /// Null until the scoring engine has evaluated this appointment for the first time.
    /// </summary>
    public int? NoShowRiskScore { get; set; }

    /// <summary>
    /// Discrete risk band derived from <see cref="NoShowRiskScore"/> (Low / Medium / High).
    /// Stored as VARCHAR so DB values are human-readable without a lookup table.
    /// Null while <see cref="NoShowRiskScore"/> is null.
    /// </summary>
    public NoShowRiskBand? NoShowRiskBand { get; set; }

    /// <summary>
    /// True when the score was produced by rule-based fallback (insufficient history,
    /// fewer than 3 prior appointments — AC-3).  UI displays an "Est." label.
    /// Null while no score has been calculated.
    /// </summary>
    public bool? IsRiskEstimated { get; set; }

    /// <summary>
    /// True when the risk score meets or exceeds the high-risk outreach threshold (EC-2).
    /// Staff workflows use this flag to initiate proactive outreach before the appointment.
    /// Null while no score has been calculated.
    /// </summary>
    public bool? RequiresOutreach { get; set; }

    /// <summary>UTC timestamp of the last risk score calculation. Used for cache staleness checks.</summary>
    public DateTime? RiskCalculatedAtUtc { get; set; }
}
