using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for <see cref="RiskConfiguration"/> (US_060 AC-3, AC-4).
///
/// Table: <c>risk_configuration</c>
///
/// Design notes:
///   - Singleton pattern: the table is seeded with exactly one row; the Admin
///     Configuration UI always reads/updates that single record.
///   - <c>ScoringParameters</c> is stored as PostgreSQL <c>jsonb</c> for flexible
///     weight-coefficient querying without schema changes.
///   - <c>HighRiskThreshold</c> and <c>MediumRiskThreshold</c> have DB-level
///     check constraints (0–100) as a last-resort guard; application-layer
///     FluentValidation is the primary enforcement point.
///   - <c>Version</c> is an optimistic-concurrency token (same pattern as
///     <see cref="SlotTemplateConfiguration"/>), preventing simultaneous admin
///     saves from silently overwriting each other.
///   - <c>UpdatedByUserId</c> is a nullable FK to <c>asp_net_users</c> for admin
///     attribution (US_060 AC-4).
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c> in
/// <see cref="UPACIP.DataAccess.ApplicationDbContext.OnModelCreating"/>; no manual
/// registration required.
/// </summary>
public sealed class RiskConfigurationConfiguration
    : IEntityTypeConfiguration<RiskConfiguration>
{
    // ── Stable seed GUID — do NOT change after first migration ───────────────
    internal static readonly Guid DefaultRiskConfigId =
        new("d0e1f2a3-b4c5-6789-abcd-000000000010");

    public void Configure(EntityTypeBuilder<RiskConfiguration> builder)
    {
        builder.ToTable("risk_configuration");

        builder.HasKey(r => r.RiskConfigId);
        builder.Property(r => r.RiskConfigId)
            .HasColumnType("uuid")
            .ValueGeneratedNever(); // singleton — PK provided by seed

        // ── Threshold columns with DB-level range checks ──────────────────────
        builder.Property(r => r.HighRiskThreshold)
            .IsRequired()
            .HasDefaultValue(75);

        builder.HasCheckConstraint(
            "ck_risk_configuration_high_threshold_range",
            "\"HighRiskThreshold\" >= 0 AND \"HighRiskThreshold\" <= 100");

        builder.Property(r => r.MediumRiskThreshold)
            .IsRequired()
            .HasDefaultValue(45);

        builder.HasCheckConstraint(
            "ck_risk_configuration_medium_threshold_range",
            "\"MediumRiskThreshold\" >= 0 AND \"MediumRiskThreshold\" <= 100");

        builder.Property(r => r.MinAppointmentsForAiScore)
            .IsRequired()
            .HasDefaultValue(3);

        builder.Property(r => r.AutoOutreach)
            .IsRequired()
            .HasDefaultValue(true);

        // ── ScoringParameters: stored as PostgreSQL jsonb ─────────────────────
        builder.Property(r => r.ScoringParameters)
            .HasColumnType("jsonb")
            .IsRequired();

        // ── Recalculation state ───────────────────────────────────────────────
        builder.Property(r => r.RecalculationPending)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(r => r.LastRecalculatedAt)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);

        // ── Optimistic-concurrency token ──────────────────────────────────────
        builder.Property(r => r.Version)
            .IsConcurrencyToken()
            .HasDefaultValue(0);

        // ── Audit columns ─────────────────────────────────────────────────────
        builder.Property(r => r.UpdatedByUserId)
            .HasColumnType("uuid")
            .IsRequired(false);

        builder.Property(r => r.UpdatedAt)
            .IsRequired()
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()");

        // Optional: FK relationship to asp_net_users for admin attribution
        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(r => r.UpdatedByUserId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull);

        // ── Seed: one default risk configuration row ──────────────────────────
        builder.HasData(new RiskConfiguration
        {
            RiskConfigId                = DefaultRiskConfigId,
            HighRiskThreshold           = 75,
            MediumRiskThreshold         = 45,
            MinAppointmentsForAiScore   = 3,
            AutoOutreach                = true,
            ScoringParameters           =
                "{\"priorNoShowsWeight\":0.50," +
                "\"cancellationHistoryWeight\":0.30," +
                "\"appointmentLeadTimeWeight\":0.20}",
            RecalculationPending        = false,
            LastRecalculatedAt          = null,
            Version                     = 0,
            UpdatedByUserId             = null,
            UpdatedAt                   = new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
        });
    }
}
