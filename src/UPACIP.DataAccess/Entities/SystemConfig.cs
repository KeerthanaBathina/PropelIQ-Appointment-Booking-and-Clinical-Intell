namespace UPACIP.DataAccess.Entities;

/// <summary>
/// Key-value system-wide configuration table (US_055 AC-3, DR-009).
///
/// <para>
/// Provides a lightweight, audited store for runtime-configurable platform settings
/// that must survive service restarts and propagate to all running instances within
/// the Redis TTL window (60 seconds).  Currently used by the queue wait-threshold
/// feature; designed to absorb future configuration keys without schema changes.
/// </para>
///
/// <para>Schema contract:</para>
/// <list type="bullet">
///   <item><see cref="ConfigKey"/> is a dot-separated namespaced string, e.g.
///         <c>queue.wait_threshold_minutes</c>.  A unique index enforces uniqueness.</item>
///   <item><see cref="ConfigValue"/> is always stored as a UTF-8 string; the service
///         layer is responsible for parsing (e.g. <c>int.Parse</c>).</item>
///   <item><see cref="UpdatedByUserId"/> is nullable — null indicates the row was
///         inserted by a migration seed and has not been changed by a user yet.</item>
/// </list>
///
/// <para>Default seed row (inserted by EF Core <c>HasData</c>):</para>
/// <code>
///   config_key  = "queue.wait_threshold_minutes"
///   config_value = "30"
/// </code>
/// </summary>
public sealed class SystemConfig
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid ConfigId { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Dot-separated namespaced configuration key (e.g. <c>queue.wait_threshold_minutes</c>).
    /// Unique — enforced by <c>ix_system_configs_config_key</c>.
    /// </summary>
    public string ConfigKey { get; set; } = string.Empty;

    /// <summary>
    /// Raw string value; parsed by the consuming service layer.
    /// For integer settings, store the decimal representation (e.g. "30").
    /// </summary>
    public string ConfigValue { get; set; } = string.Empty;

    /// <summary>Human-readable description of what this config key controls. Nullable.</summary>
    public string? Description { get; set; }

    /// <summary>
    /// FK to the <see cref="ApplicationUser"/> who last updated this row.
    /// Null for migration-seeded rows that have never been changed via the API.
    /// Nullable — ON DELETE SET NULL preserves the config row if the user is later deleted.
    /// </summary>
    public Guid? UpdatedByUserId { get; set; }

    /// <summary>UTC timestamp when this config row was first created.</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>UTC timestamp of the last value change.</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // ── Navigation properties ────────────────────────────────────────────────

    /// <summary>User who last updated this configuration entry. May be null.</summary>
    public ApplicationUser? UpdatedByUser { get; set; }
}
