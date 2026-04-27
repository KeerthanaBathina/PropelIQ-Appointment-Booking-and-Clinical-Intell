using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for <see cref="HallucinationMetric"/> (US_074 task_002).
///
/// <para>Table: <c>hallucination_metrics</c></para>
///
/// <para>Key design decisions:</para>
/// <list type="bullet">
///   <item>Unique index on <c>MetricDate</c> — one row per calendar day; prevents
///   duplicate entries and enables safe upserts from the aggregation job.</item>
///   <item>Double precision used for <c>HallucinationRate</c> and <c>TargetRate</c>
///   to support future sub-percentage analysis.</item>
/// </list>
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c> in
/// <see cref="UPACIP.DataAccess.ApplicationDbContext.OnModelCreating"/>.
/// </summary>
public sealed class HallucinationMetricConfiguration : IEntityTypeConfiguration<HallucinationMetric>
{
    public void Configure(EntityTypeBuilder<HallucinationMetric> builder)
    {
        builder.ToTable("hallucination_metrics");

        builder.HasKey(m => m.Id);

        // ── Timestamps ────────────────────────────────────────────────────────
        builder.Property(m => m.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(m => m.UpdatedAt).HasColumnType("timestamptz").IsRequired();

        // ── MetricDate — unique per day ────────────────────────────────────────
        builder.Property(m => m.MetricDate)
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(m => m.MetricDate)
            .IsUnique()
            .HasDatabaseName("ix_hallucination_metrics_metric_date");

        // ── Counts ────────────────────────────────────────────────────────────
        builder.Property(m => m.TotalVerified).IsRequired();
        builder.Property(m => m.HallucinationCount).IsRequired();
        builder.Property(m => m.PartiallySupportedCount).IsRequired();

        // ── Rate values ───────────────────────────────────────────────────────
        builder.Property(m => m.HallucinationRate).IsRequired();
        builder.Property(m => m.TargetRate).IsRequired();
    }
}
