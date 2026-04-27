using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="AiMetricAlert"/> entity (US_072 task_001).
///
/// <para>Table: <c>ai_metric_alerts</c></para>
///
/// <para>Key design decisions:</para>
/// <list type="bullet">
///   <item>Uses <c>AlertId</c> as the primary key (not <c>BaseEntity.Id</c>) — mirrors
///   the pattern used by <see cref="AuditLog"/> for non-base-entity records.</item>
///   <item><c>TrendDirection</c> stored as <c>character varying(10)</c> string for
///   human-readable schema values without lookup joins.</item>
///   <item><c>AcknowledgedByUserId</c> is a nullable FK to <c>ApplicationUser</c>.
///   <c>DeleteBehavior.Restrict</c> prevents accidental user deletion while unacknowledged
///   alerts remain.</item>
///   <item>Index on <c>GeneratedAt DESC</c> — primary access path for dashboard queries
///   that list recent alerts.</item>
///   <item>Index on <c>IsAcknowledged</c> — accelerates "pending alerts" filter queries.</item>
/// </list>
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c> in
/// <see cref="UPACIP.DataAccess.ApplicationDbContext.OnModelCreating"/>.
/// </summary>
public sealed class AiMetricAlertConfiguration : IEntityTypeConfiguration<AiMetricAlert>
{
    public void Configure(EntityTypeBuilder<AiMetricAlert> builder)
    {
        builder.ToTable("ai_metric_alerts");

        builder.HasKey(a => a.AlertId);
        builder.Property(a => a.AlertId).ValueGeneratedOnAdd();

        // ── Timestamp ─────────────────────────────────────────────────────────
        builder.Property(a => a.GeneratedAt)
            .IsRequired()
            .HasColumnType("timestamptz");

        // ── MetricName ────────────────────────────────────────────────────────
        builder.Property(a => a.MetricName)
            .IsRequired()
            .HasMaxLength(100);

        // ── Value columns ─────────────────────────────────────────────────────
        builder.Property(a => a.CurrentValue).IsRequired();
        builder.Property(a => a.TargetValue).IsRequired();

        // ── TrendDirection enum stored as string ──────────────────────────────
        builder.Property(a => a.TrendDirection)
            .HasConversion<string>()
            .HasMaxLength(10)
            .IsRequired();

        // ── Acknowledgement fields ────────────────────────────────────────────
        builder.Property(a => a.IsAcknowledged).IsRequired();

        builder.Property(a => a.AcknowledgedByUserId)
            .IsRequired(false);

        // ── FK → ApplicationUser (nullable — set only after acknowledgement) ──
        builder.HasOne(a => a.AcknowledgedBy)
            .WithMany()
            .HasForeignKey(a => a.AcknowledgedByUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Indexes ───────────────────────────────────────────────────────────
        builder.HasIndex(a => a.GeneratedAt)
            .HasDatabaseName("ix_ai_metric_alerts_generated_at");

        builder.HasIndex(a => a.IsAcknowledged)
            .HasDatabaseName("ix_ai_metric_alerts_is_acknowledged");
    }
}
