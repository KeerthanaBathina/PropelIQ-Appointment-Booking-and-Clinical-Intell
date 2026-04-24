using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

public sealed class AgreementRateMetricConfiguration : IEntityTypeConfiguration<AgreementRateMetric>
{
    public void Configure(EntityTypeBuilder<AgreementRateMetric> builder)
    {
        builder.ToTable("agreement_rate_metrics");

        builder.HasKey(a => a.MetricId);
        builder.Property(a => a.MetricId).ValueGeneratedOnAdd();

        // ─────────────────────────────────────────────────────────────────────
        // Date column — PostgreSQL date type (no time component)
        // ─────────────────────────────────────────────────────────────────────
        builder.Property(a => a.CalculationDate)
            .IsRequired()
            .HasColumnType("date");

        // ─────────────────────────────────────────────────────────────────────
        // Rate columns — precision 5, scale 4 covers [0.0000, 1.0000]
        // ─────────────────────────────────────────────────────────────────────
        builder.Property(a => a.DailyAgreementRate)
            .IsRequired()
            .HasColumnType("numeric(5,4)");

        builder.Property(a => a.Rolling30DayRate)
            .IsRequired(false)
            .HasColumnType("numeric(5,4)");

        // ─────────────────────────────────────────────────────────────────────
        // Count columns
        // ─────────────────────────────────────────────────────────────────────
        builder.Property(a => a.TotalCodesVerified).IsRequired();
        builder.Property(a => a.CodesApprovedWithoutOverride).IsRequired();
        builder.Property(a => a.CodesOverridden).IsRequired();
        builder.Property(a => a.CodesPartiallyOverridden).IsRequired();
        builder.Property(a => a.MeetsMinimumThreshold).IsRequired();

        builder.Property(a => a.CreatedAt).IsRequired();
        builder.Property(a => a.UpdatedAt).IsRequired();

        // ─────────────────────────────────────────────────────────────────────
        // Indexes (US_050 AC-2 — date-range queries for daily + rolling lookups)
        // ─────────────────────────────────────────────────────────────────────

        // Unique index enforces one row per calendar day and supports point-lookup
        // (e.g. "get today's metric") in O(log n).
        builder.HasIndex(a => a.CalculationDate)
            .IsUnique()
            .HasDatabaseName("ix_agreement_rate_metrics_calculation_date");
    }
}
