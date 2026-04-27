using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="AiAccuracyMetric"/> entity (US_072 task_001).
///
/// <para>Table: <c>ai_accuracy_metrics</c></para>
///
/// <para>Key design decisions:</para>
/// <list type="bullet">
///   <item>Composite unique index on (<c>MetricDate</c>, <c>MetricType</c>) prevents duplicate
///   daily entries and supports point-lookups by date + type in O(log n).</item>
///   <item><c>MetricType</c> stored as <c>character varying(30)</c> string for human-readable
///   schema — same pattern as other enum columns in this project.</item>
///   <item><c>Value</c> and <c>TargetValue</c> use <c>double precision</c> (PostgreSQL) for
///   percentage storage in [0, 100].</item>
/// </list>
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c> in
/// <see cref="UPACIP.DataAccess.ApplicationDbContext.OnModelCreating"/>.
/// </summary>
public sealed class AiAccuracyMetricConfiguration : IEntityTypeConfiguration<AiAccuracyMetric>
{
    public void Configure(EntityTypeBuilder<AiAccuracyMetric> builder)
    {
        builder.ToTable("ai_accuracy_metrics");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedOnAdd();

        // ── MetricDate — stored as timestamptz; callers should pass UTC midnight ──
        builder.Property(a => a.MetricDate)
            .IsRequired()
            .HasColumnType("timestamptz");

        // ── MetricType enum stored as string ──────────────────────────────────
        builder.Property(a => a.MetricType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        // ── Accuracy percentage columns ───────────────────────────────────────
        builder.Property(a => a.Value)
            .IsRequired();

        builder.Property(a => a.TargetValue)
            .IsRequired();

        // ── Sample size ───────────────────────────────────────────────────────
        builder.Property(a => a.SampleSize)
            .IsRequired();

        builder.Property(a => a.CreatedAt).IsRequired();
        builder.Property(a => a.UpdatedAt).IsRequired();

        // ── Composite unique index (MetricDate, MetricType) ───────────────────
        builder.HasIndex(a => new { a.MetricDate, a.MetricType })
            .IsUnique()
            .HasDatabaseName("ix_ai_accuracy_metrics_date_type");
    }
}
