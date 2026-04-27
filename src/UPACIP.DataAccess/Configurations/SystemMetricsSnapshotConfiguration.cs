using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for <see cref="SystemMetricsSnapshot"/> (US_058 AC-1, AC-2).
///
/// Table: <c>system_metrics_snapshots</c>
///
/// Constraints:
///   - Unique on <c>metric_date</c>: one row per UTC calendar day (safe daily upsert).
///
/// Indexes:
///   - <c>ix_system_metrics_snapshots_metric_date</c>: covers both point-lookup (today's metrics)
///     and descending date-range scans for 7-day / 30-day trend windows (AC-2, NFR-004).
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c> in
/// <see cref="UPACIP.DataAccess.ApplicationDbContext.OnModelCreating"/>; no manual registration.
/// </summary>
public sealed class SystemMetricsSnapshotConfiguration
    : IEntityTypeConfiguration<SystemMetricsSnapshot>
{
    public void Configure(EntityTypeBuilder<SystemMetricsSnapshot> builder)
    {
        builder.ToTable("system_metrics_snapshots");

        builder.HasKey(s => s.SnapshotId);
        builder.Property(s => s.SnapshotId).ValueGeneratedOnAdd();

        // ── Date column — stored as PostgreSQL `date` (no time component) ────
        builder.Property(s => s.MetricDate)
            .IsRequired()
            .HasColumnType("date");

        // ── Integer counts ────────────────────────────────────────────────────
        builder.Property(s => s.ActiveUsers)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(s => s.DailyAppointments)
            .IsRequired()
            .HasDefaultValue(0);

        // ── Percentage rate columns — numeric(5,2) covers [0.00 – 100.00] ────
        builder.Property(s => s.NoShowRate)
            .IsRequired()
            .HasColumnType("numeric(5,2)")
            .HasDefaultValue(0m);

        builder.Property(s => s.AiAgreementRate)
            .IsRequired()
            .HasColumnType("numeric(5,2)")
            .HasDefaultValue(0m);

        builder.Property(s => s.UptimePercent)
            .IsRequired()
            .HasColumnType("numeric(5,2)")
            .HasDefaultValue(100m);

        // ── Timestamps ────────────────────────────────────────────────────────
        builder.Property(s => s.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");

        builder.Property(s => s.UpdatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");

        // ── Unique + covering index on metric_date ────────────────────────────
        // Unique constraint enforces one snapshot per calendar day and doubles as the
        // primary access path for date-range trend queries (ORDER BY metric_date DESC).
        builder.HasIndex(s => s.MetricDate)
            .IsUnique()
            .HasDatabaseName("ix_system_metrics_snapshots_metric_date");
    }
}
