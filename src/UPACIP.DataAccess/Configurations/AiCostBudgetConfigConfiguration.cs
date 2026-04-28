using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using UPACIP.DataAccess.Entities;
using UPACIP.DataAccess.Enums;

namespace UPACIP.DataAccess.Configurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="AiCostBudgetConfig"/> entity
/// (US_071 TASK_001, AC-2).
///
/// <para>Table: <c>ai_cost_budget_configs</c></para>
///
/// <para>Key design decisions:</para>
/// <list type="bullet">
///   <item>One row per <see cref="AiCostBudgetConfig.Provider"/> — enforced by a unique
///   constraint on the <c>Provider</c> column.</item>
///   <item>Enum stored as <c>character varying(20)</c> string.</item>
///   <item><c>DailyBudgetThreshold</c> uses <c>numeric(10,2)</c> — supports budgets
///   up to $99,999,999.99/day with two decimal places (USD cents).</item>
///   <item>Rate card columns use <c>numeric(10,6)</c> — required precision for small
///   per-1K values like GPT-4o-mini at $0.000150.</item>
/// </list>
///
/// <para>
/// Default seed data is embedded via <c>HasData()</c> for both providers.
/// Stable GUIDs prevent duplicate inserts across migration replays.
/// </para>
///
/// Auto-discovered by <c>ApplyConfigurationsFromAssembly</c> in
/// <see cref="UPACIP.DataAccess.ApplicationDbContext.OnModelCreating"/>.
/// </summary>
public sealed class AiCostBudgetConfigConfiguration : IEntityTypeConfiguration<AiCostBudgetConfig>
{
    // ── Stable seed GUIDs — never change these values ─────────────────────────
    internal static readonly Guid OpenAiConfigId =
        new("e0f1a2b3-c4d5-6789-abcd-ef0123456701");

    internal static readonly Guid AnthropicConfigId =
        new("e0f1a2b3-c4d5-6789-abcd-ef0123456702");

    private static readonly DateTime SeedTimestamp =
        new(2026, 4, 27, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<AiCostBudgetConfig> builder)
    {
        builder.ToTable("ai_cost_budget_configs");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedOnAdd();

        // ── Provider enum — unique constraint ensures one row per provider ────
        builder.Property(c => c.Provider)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasIndex(c => c.Provider)
            .IsUnique()
            .HasDatabaseName("ix_ai_cost_budget_configs_provider");

        // ── Budget threshold — numeric(10,2) covers USD cents up to $99,999,999.99 ─
        builder.Property(c => c.DailyBudgetThreshold)
            .HasColumnType("numeric(10,2)")
            .IsRequired();

        // ── Alert toggle ──────────────────────────────────────────────────────
        builder.Property(c => c.AlertEnabled)
            .HasDefaultValue(true)
            .IsRequired();

        // ── Rate card columns — numeric(10,6) required for sub-cent per-1K values ─
        builder.Property(c => c.CostPer1kInputTokens)
            .HasColumnType("numeric(10,6)")
            .IsRequired();

        builder.Property(c => c.CostPer1kOutputTokens)
            .HasColumnType("numeric(10,6)")
            .IsRequired();

        // ── BaseEntity timestamps ─────────────────────────────────────────────
        builder.Property(c => c.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()")
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("NOW()")
            .IsRequired();

        // ── Default seed data ─────────────────────────────────────────────────
        // OpenAI GPT-4o-mini pricing (2024): $0.15/1M input, $0.60/1M output.
        // Anthropic Claude 3.5 Sonnet pricing: $3.00/1M input, $15.00/1M output.
        // Both budgets seeded with alert_enabled = true.
        builder.HasData(
            new
            {
                Id                  = OpenAiConfigId,
                Provider            = AiProvider.OpenAI,
                DailyBudgetThreshold  = 5.00m,
                AlertEnabled        = true,
                CostPer1kInputTokens  = 0.000150m,
                CostPer1kOutputTokens = 0.000600m,
                CreatedAt           = SeedTimestamp,
                UpdatedAt           = SeedTimestamp,
            },
            new
            {
                Id                  = AnthropicConfigId,
                Provider            = AiProvider.Anthropic,
                DailyBudgetThreshold  = 20.00m,
                AlertEnabled        = true,
                CostPer1kInputTokens  = 0.003000m,
                CostPer1kOutputTokens = 0.015000m,
                CreatedAt           = SeedTimestamp,
                UpdatedAt           = SeedTimestamp,
            });
    }
}
