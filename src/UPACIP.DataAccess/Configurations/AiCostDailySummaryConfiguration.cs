using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="AiCostDailySummary"/> entity
/// (US_071 TASK_001, AC-1).
///
/// <para>Table: <c>ai_cost_daily_summaries</c></para>
///
/// <para>Key design decisions:</para>
/// <list type="bullet">
///   <item>A unique composite constraint on (<see cref="AiCostDailySummary.SummaryDate"/>,
///   <see cref="AiCostDailySummary.Provider"/>, <see cref="AiCostDailySummary.RequestType"/>)
///   prevents duplicate aggregation rows and enables safe upserts from the aggregation job.</item>
///   <item>Token totals use <c>bigint</c> — daily counts across all requests can exceed
///   <c>int</c> range for high-throughput deployments.</item>
///   <item><c>TotalEstimatedCost</c> uses <c>numeric(12,6)</c> — matches
///   <see cref="AiRequestLog.EstimatedCost"/> precision; daily totals stay within range
///   for realistic cost levels.</item>
///   <item>Index on <c>SummaryDate DESC</c> covers date-range dashboard queries.</item>
/// </list>
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c> in
/// <see cref="UPACIP.DataAccess.ApplicationDbContext.OnModelCreating"/>.
/// </summary>
public sealed class AiCostDailySummaryConfiguration : IEntityTypeConfiguration<AiCostDailySummary>
{
    public void Configure(EntityTypeBuilder<AiCostDailySummary> builder)
    {
        builder.ToTable("ai_cost_daily_summaries");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedOnAdd();

        // ── Date column — PostgreSQL date (no time component) ─────────────────
        builder.Property(s => s.SummaryDate)
            .HasColumnType("date")
            .IsRequired();

        // ── Enum columns (strings) ────────────────────────────────────────────
        builder.Property(s => s.Provider)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(s => s.RequestType)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        // ── Token totals — bigint for high-volume daily aggregations ──────────
        builder.Property(s => s.TotalInputTokens)
            .HasColumnType("bigint")
            .HasDefaultValue(0L)
            .IsRequired();

        builder.Property(s => s.TotalOutputTokens)
            .HasColumnType("bigint")
            .HasDefaultValue(0L)
            .IsRequired();

        // ── Cost total — numeric(12,6) matches AiRequestLog.EstimatedCost ────
        builder.Property(s => s.TotalEstimatedCost)
            .HasColumnType("numeric(12,6)")
            .HasDefaultValue(0m)
            .IsRequired();

        // ── Counts ────────────────────────────────────────────────────────────
        builder.Property(s => s.RequestCount)
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(s => s.ApproximateRequestCount)
            .HasDefaultValue(0)
            .IsRequired();

        // ── BaseEntity timestamps ─────────────────────────────────────────────
        builder.Property(s => s.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()")
            .IsRequired();

        builder.Property(s => s.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()")
            .IsRequired();

        // ── Indexes ───────────────────────────────────────────────────────────

        // Unique composite — prevents duplicate rows; enables safe upsert (AC-1).
        builder.HasIndex(s => new { s.SummaryDate, s.Provider, s.RequestType })
            .IsUnique()
            .HasDatabaseName("ix_ai_cost_daily_summaries_date_provider_type");

        // Date-range scan — WHERE summary_date BETWEEN @start AND @end (dashboard queries).
        builder.HasIndex(s => s.SummaryDate)
            .HasDatabaseName("ix_ai_cost_daily_summaries_date")
            .IsDescending();
    }
}
