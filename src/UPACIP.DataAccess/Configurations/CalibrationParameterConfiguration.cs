using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for <see cref="CalibrationParameter"/> (US_073 task_001).
///
/// <para>Table: <c>calibration_parameters</c></para>
///
/// <para>Key design decisions:</para>
/// <list type="bullet">
///   <item><c>DataType</c> stored as <c>character varying(20)</c> string — human-readable
///   without lookup joins and consistent with other enum columns in the schema.</item>
///   <item>Unique filtered index on (<c>DataType</c>, <c>IsActive</c>) WHERE
///   <c>is_active = true</c> enforces at most one active calibration set per category
///   at the database level (task spec edge case).</item>
///   <item>Standard <c>BaseEntity</c> primary key (<c>Id</c> UUID).</item>
/// </list>
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c> in
/// <see cref="UPACIP.DataAccess.ApplicationDbContext.OnModelCreating"/>.
/// </summary>
public sealed class CalibrationParameterConfiguration : IEntityTypeConfiguration<CalibrationParameter>
{
    public void Configure(EntityTypeBuilder<CalibrationParameter> builder)
    {
        builder.ToTable("calibration_parameters");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedOnAdd();

        // ── Timestamps ────────────────────────────────────────────────────────
        builder.Property(p => p.CreatedAt)
            .IsRequired()
            .HasColumnType("timestamptz");

        builder.Property(p => p.UpdatedAt)
            .IsRequired()
            .HasColumnType("timestamptz");

        // ── DataType ──────────────────────────────────────────────────────────
        builder.Property(p => p.DataType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // ── Platt-scaling parameters ──────────────────────────────────────────
        builder.Property(p => p.Slope).IsRequired();
        builder.Property(p => p.Intercept).IsRequired();

        // ── Calibration metadata ──────────────────────────────────────────────
        builder.Property(p => p.LastCalibratedAt)
            .IsRequired()
            .HasColumnType("timestamptz");

        builder.Property(p => p.VerificationSampleSize).IsRequired();

        builder.Property(p => p.IsActive)
            .IsRequired()
            .HasDefaultValue(false);

        // ── Indexes ───────────────────────────────────────────────────────────

        // Enforce exactly one active calibration set per category (edge case).
        builder.HasIndex(p => new { p.DataType, p.IsActive })
            .HasFilter("is_active = true")
            .IsUnique()
            .HasDatabaseName("ix_calibration_parameters_data_type_active");

        // Support lookups by DataType for retrieving historical parameter sets.
        builder.HasIndex(p => p.DataType)
            .HasDatabaseName("ix_calibration_parameters_data_type");
    }
}
