using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for <see cref="CalibrationRecord"/> (US_073 task_001).
///
/// <para>Table: <c>calibration_records</c></para>
///
/// <para>Key design decisions:</para>
/// <list type="bullet">
///   <item><c>DataType</c> stored as <c>character varying(20)</c> string for schema
///   readability.</item>
///   <item>Composite index on (<c>CalibrationRunDate</c>, <c>DataType</c>) — primary
///   access path for the weekly drift analysis queries.</item>
///   <item>Index on <c>DriftDetected</c> — accelerates "show only drifted bins" dashboard
///   filter queries.</item>
/// </list>
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c> in
/// <see cref="UPACIP.DataAccess.ApplicationDbContext.OnModelCreating"/>.
/// </summary>
public sealed class CalibrationRecordConfiguration : IEntityTypeConfiguration<CalibrationRecord>
{
    public void Configure(EntityTypeBuilder<CalibrationRecord> builder)
    {
        builder.ToTable("calibration_records");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedOnAdd();

        // ── Timestamps ────────────────────────────────────────────────────────
        builder.Property(r => r.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamptz");

        builder.Property(r => r.UpdatedAt)
            .IsRequired()
            .HasColumnType("timestamptz");

        // ── CalibrationRunDate ─────────────────────────────────────────────────
        builder.Property(r => r.CalibrationRunDate)
            .IsRequired()
            .HasColumnType("timestamptz");

        // ── DataType ──────────────────────────────────────────────────────────
        builder.Property(r => r.DataType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // ── Accuracy metrics ──────────────────────────────────────────────────
        builder.Property(r => r.PredictedAccuracy).IsRequired();
        builder.Property(r => r.ActualAccuracy).IsRequired();
        builder.Property(r => r.DriftPercentage).IsRequired();

        // ── Confidence bin bounds ─────────────────────────────────────────────
        builder.Property(r => r.BinStart).IsRequired();
        builder.Property(r => r.BinEnd).IsRequired();

        // ── Sample and drift detection ────────────────────────────────────────
        builder.Property(r => r.SampleSize).IsRequired();

        builder.Property(r => r.DriftDetected)
            .IsRequired()
            .HasDefaultValue(false);

        // ── Indexes ───────────────────────────────────────────────────────────

        // Primary access path: retrieve all bins for a specific run / category.
        builder.HasIndex(r => new { r.CalibrationRunDate, r.DataType })
            .HasDatabaseName("ix_calibration_records_run_date_data_type");

        // Dashboard filter: "show only drifted bins".
        builder.HasIndex(r => r.DriftDetected)
            .HasDatabaseName("ix_calibration_records_drift_detected");
    }
}
