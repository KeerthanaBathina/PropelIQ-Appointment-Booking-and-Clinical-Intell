using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="AiMetricThreshold"/> entity (US_072 task_001).
///
/// <para>Table: <c>ai_metric_thresholds</c></para>
///
/// <para>Key design decisions:</para>
/// <list type="bullet">
///   <item>Unique index on <c>MetricName</c> enforces one threshold row per named metric,
///   enabling safe upserts and direct lookup by name.</item>
///   <item><c>MetricName</c> length capped at 100 characters — sufficient for all current
///   metric type and operation type combinations.</item>
/// </list>
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c> in
/// <see cref="UPACIP.DataAccess.ApplicationDbContext.OnModelCreating"/>.
/// </summary>
public sealed class AiMetricThresholdConfiguration : IEntityTypeConfiguration<AiMetricThreshold>
{
    public void Configure(EntityTypeBuilder<AiMetricThreshold> builder)
    {
        builder.ToTable("ai_metric_thresholds");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedOnAdd();

        // ── MetricName — unique business key ──────────────────────────────────
        builder.Property(t => t.MetricName)
            .IsRequired()
            .HasMaxLength(100);

        // ── Threshold values ──────────────────────────────────────────────────
        builder.Property(t => t.TargetValue).IsRequired();
        builder.Property(t => t.WarningValue).IsRequired();
        builder.Property(t => t.IsEnabled).IsRequired();

        builder.Property(t => t.CreatedAt).IsRequired();
        builder.Property(t => t.UpdatedAt).IsRequired();

        // ── Unique index on MetricName ─────────────────────────────────────────
        builder.HasIndex(t => t.MetricName)
            .IsUnique()
            .HasDatabaseName("ix_ai_metric_thresholds_metric_name");
    }
}
