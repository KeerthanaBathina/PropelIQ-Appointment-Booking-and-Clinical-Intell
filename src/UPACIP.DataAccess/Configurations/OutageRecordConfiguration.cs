using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for <see cref="OutageRecord"/> (US_083 task_001, AC-3).
///
/// Table: <c>outage_records</c>
///
/// Indexes:
///   - <c>ix_outage_records_started_at</c>: covers active-outage lookups and chronological
///     outage history queries.
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c> in
/// <see cref="UPACIP.DataAccess.ApplicationDbContext.OnModelCreating"/>; no manual registration.
/// </summary>
public sealed class OutageRecordConfiguration : IEntityTypeConfiguration<OutageRecord>
{
    public void Configure(EntityTypeBuilder<OutageRecord> builder)
    {
        builder.ToTable("outage_records");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).ValueGeneratedOnAdd();

        builder.Property(o => o.StartedAt)
            .IsRequired()
            .HasColumnType("timestamptz");

        builder.Property(o => o.ResolvedAt)
            .HasColumnType("timestamptz");

        builder.Property(o => o.AffectedServices)
            .IsRequired()
            .HasMaxLength(1000)
            .HasDefaultValue(string.Empty);

        builder.Property(o => o.ImpactLevel)
            .IsRequired()
            .HasMaxLength(20)
            .HasDefaultValue("Minor");

        builder.Property(o => o.AlertSentAt)
            .HasColumnType("timestamptz");

        builder.HasIndex(o => o.StartedAt)
            .HasDatabaseName("ix_outage_records_started_at");
    }
}
