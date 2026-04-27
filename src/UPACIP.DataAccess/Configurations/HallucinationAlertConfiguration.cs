using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for <see cref="HallucinationAlert"/> (US_074 task_002).
///
/// <para>Table: <c>hallucination_alerts</c></para>
///
/// <para>Key design decisions:</para>
/// <list type="bullet">
///   <item>Uses <c>AlertId</c> as the dedicated PK — mirrors the append-only alert
///   pattern of <see cref="CalibrationDriftAlert"/> and <see cref="AiMetricAlert"/>.</item>
///   <item><c>Recommendation</c> max 500 characters — sufficient for structured
///   recommendation text including rate figures.</item>
///   <item><c>AcknowledgedByUserId</c> FK to <c>ApplicationUser</c> with
///   <c>DeleteBehavior.Restrict</c> — prevents user deletion while unacknowledged
///   alerts exist.</item>
///   <item>Index on <c>IsAcknowledged</c> — accelerates pending-alert dashboard queries.</item>
///   <item>Index on <c>GeneratedAt DESC</c> — primary access path for recent-alert listing.</item>
///   <item>Index on <c>IsRetroactive</c> — supports filtering retroactive vs. scheduled alerts.</item>
/// </list>
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c> in
/// <see cref="UPACIP.DataAccess.ApplicationDbContext.OnModelCreating"/>.
/// </summary>
public sealed class HallucinationAlertConfiguration : IEntityTypeConfiguration<HallucinationAlert>
{
    public void Configure(EntityTypeBuilder<HallucinationAlert> builder)
    {
        builder.ToTable("hallucination_alerts");

        builder.HasKey(a => a.AlertId);
        builder.Property(a => a.AlertId).ValueGeneratedOnAdd();

        // ── Timestamps ────────────────────────────────────────────────────────
        builder.Property(a => a.GeneratedAt)
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(a => a.AcknowledgedAt)
            .HasColumnType("timestamptz")
            .IsRequired(false);

        // ── Rate values ───────────────────────────────────────────────────────
        builder.Property(a => a.CurrentRate).IsRequired();
        builder.Property(a => a.TargetRate).IsRequired();

        // ── Recommendation text ───────────────────────────────────────────────
        builder.Property(a => a.Recommendation)
            .HasMaxLength(500)
            .IsRequired();

        // ── Flags ─────────────────────────────────────────────────────────────
        builder.Property(a => a.IsRetroactive).IsRequired();
        builder.Property(a => a.IsAcknowledged).IsRequired();
        builder.Property(a => a.AcknowledgedByUserId).IsRequired(false);

        // ── FK: AcknowledgedBy → ApplicationUser ──────────────────────────────
        builder.HasOne(a => a.AcknowledgedBy)
            .WithMany()
            .HasForeignKey(a => a.AcknowledgedByUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict);

        // ── Indexes ───────────────────────────────────────────────────────────

        // Most-recent-first listing on the admin dashboard.
        builder.HasIndex(a => a.GeneratedAt)
            .HasDatabaseName("ix_hallucination_alerts_generated_at");

        // Efficient pending-alert count and filter.
        builder.HasIndex(a => a.IsAcknowledged)
            .HasDatabaseName("ix_hallucination_alerts_is_acknowledged");

        // Retroactive vs. scheduled filtering.
        builder.HasIndex(a => a.IsRetroactive)
            .HasDatabaseName("ix_hallucination_alerts_is_retroactive");
    }
}
