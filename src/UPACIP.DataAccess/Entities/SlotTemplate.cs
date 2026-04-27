namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Admin-configurable weekly slot template for a specific provider and day of the week
/// (US_059 AC-1, AC-2).
///
/// <para>
/// A <see cref="SlotTemplate"/> is a header record that groups one or more
/// <see cref="SlotTemplateBlock"/> child records.  Each block defines a time window within
/// the day with an appointment type and availability flag.  This enables fine-grained
/// per-hour customisation beyond the coarser <c>ProviderAvailabilityTemplate</c> records.
/// </para>
///
/// <para>
/// The <see cref="Version"/> field is used as an EF Core optimistic-concurrency token to
/// prevent lost-update anomalies when two admins save the same template concurrently.
/// </para>
///
/// <para>
/// A composite unique constraint on (<c>ProviderId</c>, <c>DayOfWeek</c>) ensures at most
/// one active template per provider/day combination.
/// </para>
///
/// <para>
/// Does NOT inherit <see cref="BaseEntity"/>: uses a dedicated <c>SlotTemplateId</c> PK
/// matching the task specification.
/// </para>
/// </summary>
public sealed class SlotTemplate
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid SlotTemplateId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// FK to <see cref="ApplicationUser"/> (staff/provider).
    /// A unique constraint on (ProviderId, DayOfWeek) prevents duplicate templates.
    /// </summary>
    public Guid ProviderId { get; set; }

    /// <summary>
    /// Day of the week this template applies to, using <see cref="System.DayOfWeek"/> integer values
    /// (0 = Sunday, 1 = Monday, …, 6 = Saturday).
    /// </summary>
    public int DayOfWeek { get; set; }

    /// <summary>
    /// Optimistic-concurrency row version token.
    /// EF Core includes <c>Version</c> in every <c>UPDATE WHERE</c> clause; a stale write
    /// throws <c>DbUpdateConcurrencyException</c>.
    /// </summary>
    public int Version { get; set; }

    /// <summary>UTC timestamp when this template was first created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the most recent save.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ── Navigation properties ─────────────────────────────────────────────────

    /// <summary>Provider associated with this template.</summary>
    public ApplicationUser Provider { get; set; } = null!;

    /// <summary>
    /// Child time blocks that define per-hour appointment types and availability
    /// within this template. Cascade-deleted when the template is removed.
    /// </summary>
    public ICollection<SlotTemplateBlock> Blocks { get; set; } = [];

    /// <summary>
    /// Appointments that were originally booked against this template
    /// (nullable FK — set to null on template deletion for traceability).
    /// </summary>
    public ICollection<Appointment> Appointments { get; set; } = [];
}
