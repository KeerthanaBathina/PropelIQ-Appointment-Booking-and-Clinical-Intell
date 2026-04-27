using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="SystemConfig"/> entity (US_055 AC-3).
///
/// <para>
/// Also seeds the default wait-time-threshold row via <c>HasData()</c> so that the
/// value is present in all environments as soon as the migration is applied —
/// matching the pattern used by <c>RoleSeedConfiguration</c>.
/// </para>
///
/// <para>
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c> in
/// <see cref="ApplicationDbContext.OnModelCreating"/>; no manual registration needed.
/// </para>
/// </summary>
internal sealed class SystemConfigConfiguration : IEntityTypeConfiguration<SystemConfig>
{
    /// <summary>
    /// Stable deterministic GUID for the default wait-threshold seed row.
    /// Never change this value — migrations are snapshot-diffed against it.
    /// </summary>
    internal static readonly Guid DefaultThresholdConfigId =
        new("d4e5f6a7-b8c9-0d1e-2f3a-4b5c6d7e8f90");

    /// <summary>
    /// The namespaced config key used by <see cref="UPACIP.Service.Queue.QueueService"/>
    /// and the Redis caching layer to look up the current threshold (US_055 AC-3).
    /// Must be kept in sync with <c>QueueService.ThresholdConfigKey</c>.
    /// </summary>
    internal const string WaitThresholdKey = "queue.wait_threshold_minutes";

    public void Configure(EntityTypeBuilder<SystemConfig> builder)
    {
        builder.ToTable("system_configs");

        builder.HasKey(c => c.ConfigId);
        builder.Property(c => c.ConfigId).ValueGeneratedOnAdd();

        builder.Property(c => c.ConfigKey)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(c => c.ConfigValue)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(c => c.Description)
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(c => c.UpdatedByUserId)
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(c => c.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()")
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()")
            .IsRequired();

        // Unique index: O(1) lookup by config key (DR-009).
        builder.HasIndex(c => c.ConfigKey)
            .IsUnique()
            .HasDatabaseName("ix_system_configs_config_key");

        // FK: nullable — ON DELETE SET NULL preserves config rows when a user is deleted.
        builder.HasOne(c => c.UpdatedByUser)
            .WithMany()
            .HasForeignKey(c => c.UpdatedByUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // ── Seed default threshold (US_055 AC-3) ──────────────────────────────
        // Inserted by the AddSystemConfigTable migration. Idempotent — EF Core will
        // detect changes to this data in subsequent migrations automatically.
        builder.HasData(new SystemConfig
        {
            ConfigId      = DefaultThresholdConfigId,
            ConfigKey     = WaitThresholdKey,
            ConfigValue   = "30",
            Description   = "Wait time threshold in minutes for staff alerts (default: 30). " +
                            "Valid range: 5–120. Updated via PUT /api/queue/config/threshold.",
            UpdatedByUserId = null,
            CreatedAt     = new DateTime(2026, 4, 24, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt     = new DateTime(2026, 4, 24, 0, 0, 0, DateTimeKind.Utc),
        });
    }
}
