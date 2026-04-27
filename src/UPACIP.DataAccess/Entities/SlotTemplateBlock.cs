namespace UPACIP.DataAccess.Entities;

/// <summary>
/// A time block within a <see cref="SlotTemplate"/> defining an appointment window,
/// type, and availability for a specific hour range on the parent template's day (US_059 AC-1).
///
/// <para>
/// Multiple <see cref="SlotTemplateBlock"/> records belong to a single
/// <see cref="SlotTemplate"/> (one-to-many).  Blocks are cascade-deleted when their
/// parent template is removed.
/// </para>
///
/// <para>
/// A check constraint on the <c>slot_template_blocks</c> table enforces
/// <c>start_time &lt; end_time</c> at the database level (DR-009).
/// </para>
///
/// <para>
/// Does NOT inherit <see cref="BaseEntity"/>: uses a dedicated <c>BlockId</c> PK and
/// only needs <c>CreatedAt</c> (blocks are immutable once created — template changes
/// replace blocks wholesale via cascade delete + re-insert).
/// </para>
/// </summary>
public sealed class SlotTemplateBlock
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid BlockId { get; set; } = Guid.NewGuid();

    /// <summary>FK to the parent <see cref="SlotTemplate"/>. Cascade-deleted with the template.</summary>
    public Guid SlotTemplateId { get; set; }

    /// <summary>Time of day when this appointment block begins. Must be before <see cref="EndTime"/>.</summary>
    public TimeOnly StartTime { get; set; }

    /// <summary>Time of day when this appointment block ends. Must be after <see cref="StartTime"/>.</summary>
    public TimeOnly EndTime { get; set; }

    /// <summary>
    /// Appointment type offered during this block (e.g. "General Checkup", "Follow-up").
    /// Max 50 characters.
    /// </summary>
    public string AppointmentType { get; set; } = string.Empty;

    /// <summary>
    /// Whether this block is available for booking.
    /// Inactive blocks are excluded from the slot grid without deleting the block record.
    /// Default: <c>true</c>.
    /// </summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>UTC timestamp when this block was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // ── Navigation property ──────────────────────────────────────────────────

    /// <summary>The parent slot template this block belongs to.</summary>
    public SlotTemplate SlotTemplate { get; set; } = null!;
}
