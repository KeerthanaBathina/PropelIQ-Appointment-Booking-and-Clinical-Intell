using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for <see cref="AbMetricRecordEntity"/>
/// (US_080 task_001, AC-2, AIR-O10).
///
/// <para>Table: <c>ab_metric_records</c></para>
///
/// <para>Key design decisions:</para>
/// <list type="bullet">
///   <item>Composite index on (<c>ExperimentId</c>, <c>Variant</c>, <c>CreatedAt</c>) —
///     primary access path for per-experiment, per-variant metric aggregation queries.</item>
///   <item>Index on <c>ExperimentId</c> alone — covers join lookups from the experiment
///     side when computing total counts.</item>
///   <item><c>EstimatedCost</c> uses <c>numeric(18,8)</c> to avoid floating-point
///     accumulation errors over many rows.</item>
///   <item>Append-only — no UPDATE or DELETE paths are exposed on this table.</item>
/// </list>
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c>.
/// </summary>
public sealed class AbMetricRecordConfiguration : IEntityTypeConfiguration<AbMetricRecordEntity>
{
    public void Configure(EntityTypeBuilder<AbMetricRecordEntity> builder)
    {
        builder.ToTable("ab_metric_records");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        // ── Timestamps ────────────────────────────────────────────────────────
        builder.Property(m => m.CreatedAt).HasColumnType("timestamptz").IsRequired();

        // ── Scalar columns ────────────────────────────────────────────────────
        builder.Property(m => m.ExperimentId).IsRequired();

        builder.Property(m => m.Variant)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(m => m.Accuracy).IsRequired(false);
        builder.Property(m => m.LatencyMs).IsRequired();
        builder.Property(m => m.TokensUsed).IsRequired();

        builder.Property(m => m.EstimatedCost)
            .HasColumnType("numeric(18,8)")
            .IsRequired();

        builder.Property(m => m.RequestType)
            .HasMaxLength(100)
            .IsRequired();

        // ── FK to parent experiment ───────────────────────────────────────────
        builder.HasOne(m => m.Experiment)
            .WithMany()
            .HasForeignKey(m => m.ExperimentId)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Indexes ───────────────────────────────────────────────────────────

        // Composite — primary aggregation path: "all metrics for experiment X, variant Y, ordered by time".
        builder.HasIndex(m => new { m.ExperimentId, m.Variant, m.CreatedAt })
            .HasDatabaseName("ix_ab_metric_records_experiment_variant_created");

        // ExperimentId alone — supports total-count joins and experiment-level summaries.
        builder.HasIndex(m => m.ExperimentId)
            .HasDatabaseName("ix_ab_metric_records_experiment_id");
    }
}
