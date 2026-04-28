using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for <see cref="AbExperimentEntity"/>
/// (US_080 task_001, AC-1, AIR-O10).
///
/// <para>Table: <c>ab_experiments</c></para>
///
/// <para>Key design decisions:</para>
/// <list type="bullet">
///   <item>Filtered unique index on <c>Status = 'Active'</c> — only one experiment
///     may be active at any time; enforced at the database level.</item>
///   <item>Index on <c>StartDate</c> — accelerates chronological list queries.</item>
///   <item><c>Status</c> stored as <c>character varying(20)</c> string for readability
///     in raw SQL dashboards.</item>
/// </list>
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c>.
/// </summary>
public sealed class AbExperimentConfiguration : IEntityTypeConfiguration<AbExperimentEntity>
{
    public void Configure(EntityTypeBuilder<AbExperimentEntity> builder)
    {
        builder.ToTable("ab_experiments");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        // ── Timestamps ────────────────────────────────────────────────────────
        builder.Property(e => e.CreatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnType("timestamptz").IsRequired();
        builder.Property(e => e.StartDate).HasColumnType("timestamptz").IsRequired();
        builder.Property(e => e.EndDate).HasColumnType("timestamptz").IsRequired(false);

        // ── String properties ─────────────────────────────────────────────────
        builder.Property(e => e.ControlModelId)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.CandidateModelId)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(e => e.Status)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(e => e.Description)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(e => e.CreatedByUserId)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.TrafficSplitPercentage).IsRequired();

        // ── Indexes ───────────────────────────────────────────────────────────

        // Filtered unique: only one Active experiment permitted at a time (AC-1 edge case).
        builder.HasIndex(e => e.Status)
            .HasDatabaseName("ix_ab_experiments_active_unique")
            .HasFilter("\"status\" = 'Active'")
            .IsUnique();

        // StartDate index for chronological list queries.
        builder.HasIndex(e => e.StartDate)
            .HasDatabaseName("ix_ab_experiments_start_date")
            .IsDescending();
    }
}
