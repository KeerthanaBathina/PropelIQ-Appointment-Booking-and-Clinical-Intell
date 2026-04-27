using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for <see cref="CalibrationDriftAlert"/> (US_073 task_001).
///
/// <para>Table: <c>calibration_drift_alerts</c></para>
///
/// <para>Key design decisions:</para>
/// <list type="bullet">
///   <item>Uses <c>AlertId</c> as the dedicated PK — mirrors the append-only alert
///   pattern of <see cref="AiMetricAlert"/> and <see cref="AuditLog"/>.</item>
///   <item><c>DataType</c> stored as <c>character varying(20)</c> string.</item>
///   <item><c>AcknowledgedByUserId</c> is a nullable FK to <c>ApplicationUser</c>.
///   <c>DeleteBehavior.Restrict</c> prevents accidental user deletion while active
///   drift alerts remain unacknowledged.</item>
///   <item>Index on <c>GeneratedAt DESC</c> — primary access path for recent-alert queries.</item>
///   <item>Index on <c>IsAcknowledged</c> — accelerates "pending alerts" filter queries.</item>
/// </list>
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c> in
/// <see cref="UPACIP.DataAccess.ApplicationDbContext.OnModelCreating"/>.
/// </summary>
public sealed class CalibrationDriftAlertConfiguration : IEntityTypeConfiguration<CalibrationDriftAlert>
{
    public void Configure(EntityTypeBuilder<CalibrationDriftAlert> builder)
    {
        builder.ToTable("calibration_drift_alerts");

        builder.HasKey(a => a.AlertId);
        builder.Property(a => a.AlertId).ValueGeneratedOnAdd();

        // ── Timestamps ────────────────────────────────────────────────────────
        builder.Property(a => a.GeneratedAt)
            .IsRequired()
            .HasColumnType("timestamptz");

        builder.Property(a => a.AcknowledgedAt)
            .IsRequired(false)
            .HasColumnType("timestamptz");

        // ── DataType ──────────────────────────────────────────────────────────
        builder.Property(a => a.DataType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // ── Accuracy / drift values ───────────────────────────────────────────
        builder.Property(a => a.PredictedAccuracy).IsRequired();
        builder.Property(a => a.ActualAccuracy).IsRequired();
        builder.Property(a => a.DriftPercentage).IsRequired();

        // ── Acknowledgement ───────────────────────────────────────────────────
        builder.Property(a => a.IsAcknowledged).IsRequired();
        builder.Property(a => a.AcknowledgedByUserId).IsRequired(false);

        // ── FK: AcknowledgedBy → ApplicationUser ──────────────────────────────
        // Restrict deletion of users who have acknowledged alerts to preserve the
        // admin audit trail.
        builder.HasOne(a => a.AcknowledgedBy)
            .WithMany()
            .HasForeignKey(a => a.AcknowledgedByUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Indexes ───────────────────────────────────────────────────────────

        // Most-recent-first listing on the admin dashboard.
        builder.HasIndex(a => a.GeneratedAt)
            .HasDatabaseName("ix_calibration_drift_alerts_generated_at");

        // Efficient "pending alerts" count and filter.
        builder.HasIndex(a => a.IsAcknowledged)
            .HasDatabaseName("ix_calibration_drift_alerts_is_acknowledged");

        // Per-category alert history.
        builder.HasIndex(a => a.DataType)
            .HasDatabaseName("ix_calibration_drift_alerts_data_type");
    }
}
