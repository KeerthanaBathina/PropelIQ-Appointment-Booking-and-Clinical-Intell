using System.ComponentModel.DataAnnotations;
using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Tracks a patient's position in the waiting-room queue for a given appointment.
/// Each appointment has at most one associated queue entry (one-to-one relationship).
/// <para>
/// US_052 additions: NoShow / ArrivedLate / Cancelled statuses, cancellation timestamp,
/// override reason + actor, and an optimistic-concurrency Version token (TR-015).
/// </para>
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

    // ── US_052 — Cancellation fields (AC-3) ───────────────────────────────────

    /// <summary>UTC timestamp when the slot was cancelled by staff. Null when not cancelled.</summary>
    public DateTime? CancelledAt { get; set; }

    // ── US_052 — No-show override fields (edge case) ─────────────────────────

    /// <summary>
    /// Staff-provided reason when overriding a no-show to arrived-late status (min 10 chars).
    /// Null for entries that were never overridden.
    /// </summary>
    public string? OverrideReason { get; set; }

    /// <summary>
    /// Identity (ApplicationUser.Id) of the staff member who performed the override.
    /// Null when no override has occurred.
    /// </summary>
    public Guid? OverriddenByUserId { get; set; }

    // ── Optimistic concurrency (TR-015) ───────────────────────────────────────

    /// <summary>
    /// Row-version counter incremented on every UPDATE.
    /// EF Core uses this as a concurrency token — stale-write attempts throw
    /// <c>DbUpdateConcurrencyException</c> (TR-015).
    /// </summary>
    [ConcurrencyCheck]
    public int Version { get; set; }

    // ── US_054 — Priority queue position ─────────────────────────────────────

    /// <summary>
    /// Stored 1-based display position within today's queue.
    /// Recalculated by QueueService whenever priority or reorder operations occur (US_054 AC-1, AC-2).
    /// Urgent patients occupy the lowest position numbers (sorted by arrival_timestamp within their tier);
    /// normal patients follow. Value is 0 until first priority calculation.
    /// </summary>
    public int QueuePosition { get; set; }

    // ── US_055 — Auto no-show detection flags ─────────────────────────────────

    /// <summary>
    /// True when the no-show status was set automatically by <c>NoShowDetectionService</c>
    /// (system-generated, not staff-initiated). Used to render the "Auto-marked" badge
    /// in the queue UI (US_055 AC-4).
    /// </summary>
    public bool IsAutoNoShow { get; set; }

    /// <summary>
    /// True when the auto no-show was detected during outage recovery (service restarted
    /// after the scheduled window had already passed — "delayed detection" edge case).
    /// Set alongside <see cref="IsAutoNoShow"/> by the startup recovery scan in
    /// <c>NoShowDetectionService</c>.
    /// </summary>
    public bool IsDelayedDetection { get; set; }

    // ─────────────────────────────────────────────────────────────────────────
    // Navigation properties
    // ─────────────────────────────────────────────────────────────────────────

    public Appointment Appointment { get; set; } = null!;
}
