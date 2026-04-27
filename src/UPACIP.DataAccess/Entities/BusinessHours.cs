namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Clinic-wide business hours definition for a single day of the week (US_059 AC-3).
///
/// <para>
/// One row per day of the week (0=Sunday … 6=Saturday), seeded with default hours
/// (Mon–Fri 08:00–17:00, Sat 09:00–13:00, Sun closed).  The booking interface enforces
/// these hours so that patients cannot select slots outside the clinic's open window.
/// </para>
///
/// <para>
/// When <see cref="IsClosed"/> is <c>true</c>, <see cref="OpenTime"/> and
/// <see cref="CloseTime"/> are <c>null</c> and no appointment slots are generated for that day.
/// A database check constraint enforces that open/close times are provided when not closed.
/// </para>
///
/// <para>
/// Does NOT inherit <see cref="BaseEntity"/>: uses a dedicated <c>BusinessHoursId</c> PK.
/// Only <c>UpdatedAt</c> is needed — rows are seeded once and subsequently updated by admins.
/// </para>
/// </summary>
public sealed class BusinessHours
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid BusinessHoursId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Day of the week using <see cref="System.DayOfWeek"/> integer values
    /// (0 = Sunday, 1 = Monday, …, 6 = Saturday).
    /// A unique constraint ensures one row per day.
    /// </summary>
    public int DayOfWeek { get; set; }

    /// <summary>
    /// Time of day the clinic opens on this day.
    /// Null when <see cref="IsClosed"/> is <c>true</c>.
    /// </summary>
    public TimeOnly? OpenTime { get; set; }

    /// <summary>
    /// Time of day the clinic closes on this day.
    /// Null when <see cref="IsClosed"/> is <c>true</c>.
    /// </summary>
    public TimeOnly? CloseTime { get; set; }

    /// <summary>
    /// When <c>true</c>, the clinic is closed on this day and no appointment slots are generated.
    /// </summary>
    public bool IsClosed { get; set; }

    /// <summary>UTC timestamp of the most recent admin update to this row.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>FK to <see cref="ApplicationUser"/> who last updated this record. Nullable.</summary>
    public Guid? UpdatedByUserId { get; set; }

    // ── Navigation property ──────────────────────────────────────────────────

    /// <summary>User who last updated this record. Null for system/seed-created rows.</summary>
    public ApplicationUser? UpdatedByUser { get; set; }
}
