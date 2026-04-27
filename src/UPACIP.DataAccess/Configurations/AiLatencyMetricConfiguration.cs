using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="AiLatencyMetric"/> entity (US_072 task_001).
///
/// <para>Table: <c>ai_latency_metrics</c></para>
///
/// <para>Key design decisions:</para>
/// <list type="bullet">
///   <item>Composite unique index on (<c>MetricDate</c>, <c>OperationType</c>) prevents duplicate
///   daily entries and supports point-lookups by date + operation in O(log n).</item>
///   <item><c>OperationType</c> stored as <c>character varying(20)</c> string.</item>
///   <item>Latency columns use <c>double precision</c> for millisecond values.</item>
/// </list>
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c> in
/// <see cref="UPACIP.DataAccess.ApplicationDbContext.OnModelCreating"/>.
/// </summary>
public sealed class AiLatencyMetricConfiguration : IEntityTypeConfiguration<AiLatencyMetric>
{
    public void Configure(EntityTypeBuilder<AiLatencyMetric> builder)
    {
        builder.ToTable("ai_latency_metrics");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd();

        // ── MetricDate ────────────────────────────────────────────────────────
        builder.Property(a => a.MetricDate)
            .IsRequired()
            .HasColumnType("timestamptz");

        // ── OperationType enum stored as string ───────────────────────────────
        builder.Property(a => a.OperationType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // ── Latency columns ───────────────────────────────────────────────────
        builder.Property(a => a.P50Milliseconds).IsRequired();
        builder.Property(a => a.P95Milliseconds).IsRequired();
        builder.Property(a => a.TargetP95Milliseconds).IsRequired();

        // ── Sample size ───────────────────────────────────────────────────────
        builder.Property(a => a.SampleSize).IsRequired();

        builder.Property(a => a.CreatedAt).IsRequired();
        builder.Property(a => a.UpdatedAt).IsRequired();

        // ── Composite unique index (MetricDate, OperationType) ────────────────
        builder.HasIndex(a => new { a.MetricDate, a.OperationType })
            .IsUnique()
            .HasDatabaseName("ix_ai_latency_metrics_date_operation");
    }
}
