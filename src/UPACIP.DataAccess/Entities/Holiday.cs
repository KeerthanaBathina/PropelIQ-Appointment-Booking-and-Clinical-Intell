namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Clinic holiday definition that blocks appointment slots for a specific date (US_059 AC-4).
///
/// <para>
/// When a holiday is active (i.e. <see cref="DeletedAt"/> is <c>null</c>), no appointment
/// slots are generated for <see cref="Date"/>, and any existing bookings for that date are
/// flagged for staff review.
/// </para>
///
/// <para>
/// Recurring holidays (<see cref="IsRecurring"/> = <c>true</c>) automatically block the
/// same calendar day every year (e.g. Christmas = December 25 each year).
/// </para>
///
/// <para>
/// Half-day holidays (<see cref="IsHalfDay"/> = <c>true</c>) block the afternoon only;
/// morning slots remain bookable.
/// </para>
///
/// <para>
/// Soft-delete: setting <see cref="DeletedAt"/> to a non-null timestamp deactivates the
/// holiday without removing the audit record.  A partial unique index on <c>Date WHERE
/// deleted_at IS NULL</c> prevents duplicate active holidays for the same date.
/// </para>
///
/// <para>
/// Does NOT inherit <see cref="BaseEntity"/>: uses a dedicated <c>HolidayId</c> PK.
/// <c>UpdatedAt</c> is not needed; once created a holiday is either active or soft-deleted.
/// </para>
/// </summary>
public sealed class Holiday
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid HolidayId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The calendar date this holiday applies to.
    /// A partial unique constraint (<c>WHERE deleted_at IS NULL</c>) prevents duplicate
    /// active holidays on the same date.
    /// </summary>
    public DateOnly Date { get; set; }

    /// <summary>
    /// Human-readable holiday name shown in the Admin UI and staff calendar (e.g. "Christmas Day").
    /// Max 200 characters.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// When <c>true</c>, this holiday recurs on the same calendar day every year
    /// (month + day only, year-agnostic).
    /// </summary>
    public bool IsRecurring { get; set; }

    /// <summary>
    /// When <c>true</c>, only the afternoon is blocked; morning slots remain available.
    /// </summary>
    public bool IsHalfDay { get; set; }

    /// <summary>FK to the <see cref="ApplicationUser"/> who created this holiday record. Nullable.</summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>UTC timestamp when this holiday was created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// UTC timestamp of soft-deletion.
    /// <c>null</c> = holiday is active.
    /// Non-null = holiday is deactivated (excluded from booking validation).
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    // ── Navigation property ──────────────────────────────────────────────────

    /// <summary>User who created this holiday record. Null for seed/system-created rows.</summary>
    public ApplicationUser? CreatedByUser { get; set; }
}
