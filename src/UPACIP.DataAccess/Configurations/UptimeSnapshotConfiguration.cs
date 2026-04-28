using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for <see cref="UptimeSnapshot"/> (US_083 task_001, AC-1).
///
/// Table: <c>uptime_snapshots</c>
///
/// Indexes:
///   - <c>ix_uptime_snapshots_timestamp</c>: covers rolling-window range queries
///     (SELECT WHERE timestamp &gt;= cutoff) for 30-day uptime computation and
///     90-day retention pruning.
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c> in
/// <see cref="UPACIP.DataAccess.ApplicationDbContext.OnModelCreating"/>; no manual registration.
/// </summary>
public sealed class UptimeSnapshotConfiguration : IEntityTypeConfiguration<UptimeSnapshot>
{
    public void Configure(EntityTypeBuilder<UptimeSnapshot> builder)
    {
        builder.ToTable("uptime_snapshots");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedOnAdd();

        builder.Property(s => s.Timestamp)
            .IsRequired()
            .HasColumnType("timestamptz");

        builder.Property(s => s.IsHealthy)
            .IsRequired();

        builder.Property(s => s.DependencyStatusesJson)
            .IsRequired()
            .HasColumnType("text")
            .HasDefaultValue("{}");

        builder.Property(s => s.IsMaintenanceWindow)
            .IsRequired()
            .HasDefaultValue(false);

        builder.HasIndex(s => s.Timestamp)
            .HasDatabaseName("ix_uptime_snapshots_timestamp");
    }
}
