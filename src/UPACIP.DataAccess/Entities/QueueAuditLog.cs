namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Queue-specific audit trail entry capturing position and priority changes (US_054 AC-3, TR-028).
///
/// Separate from the general-purpose <see cref="AuditLog"/> table to allow richer
/// queue-domain fields (original/new position and priority) without widening the shared
/// audit schema. Append-only — no update or delete paths are exposed.
///
/// Populated by QueueService.SetPriorityAsync and QueueService.ReorderQueueAsync.
/// </summary>
public sealed class QueueAuditLog
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid AuditId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Type of queue operation: "PRIORITY_CHANGE" or "REORDER".
    /// </summary>
    public string ActionType { get; set; } = string.Empty;

    /// <summary>FK to the <see cref="QueueEntry"/> that was modified.</summary>
    public Guid QueueId { get; set; }

    /// <summary>
    /// FK to the <see cref="ApplicationUser"/> (staff) who performed the action.
    /// Nullable — preserved as null for system-generated events and when the user is
    /// later deleted (ON DELETE SET NULL, DR-016).
    /// </summary>
    public Guid? StaffUserId { get; set; }

    /// <summary>1-based queue position before the operation. 0 when not applicable.</summary>
    public int OriginalPosition { get; set; }

    /// <summary>1-based queue position after the operation.</summary>
    public int NewPosition { get; set; }

    /// <summary>Priority string ("normal" | "urgent") before the operation.</summary>
    public string OriginalPriority { get; set; } = string.Empty;

    /// <summary>Priority string ("normal" | "urgent") after the operation.</summary>
    public string NewPriority { get; set; } = string.Empty;

    /// <summary>UTC timestamp when this audit record was inserted.</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    // ── Navigation properties ─────────────────────────────────────────────────

    public QueueEntry? QueueEntry { get; set; }
    public ApplicationUser? StaffUser { get; set; }
}
