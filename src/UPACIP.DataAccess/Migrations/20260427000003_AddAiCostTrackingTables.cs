using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <summary>
    /// Creates three tables to support AI cost tracking, budget configuration, and
    /// daily cost aggregations (US_071 TASK_001, AC-1, AC-2).
    ///
    /// Changes applied by <see cref="Up"/>:
    ///   1. <c>ai_request_logs</c> — immutable per-request cost records (append-only).
    ///   2. <c>ai_cost_budget_configs</c> — per-provider budget thresholds and rate cards;
    ///      seeded with default values for OpenAI GPT-4o-mini and Anthropic Claude 3.5 Sonnet.
    ///   3. <c>ai_cost_daily_summaries</c> — pre-aggregated daily cost rollup by provider
    ///      and request type; unique composite index enables safe upserts.
    ///
    /// Pricing references:
    ///   - OpenAI GPT-4o-mini:          $0.000150/1K input, $0.000600/1K output
    ///   - Anthropic Claude 3.5 Sonnet: $0.003000/1K input, $0.015000/1K output
    ///
    /// <see cref="Down"/> drops all three tables to support full rollback (DR-029).
    /// </summary>
    public partial class AddAiCostTrackingTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Table: ai_request_logs (US_071 TASK_001, AC-1) ───────────────────────────────
            // Immutable append-only per-request AI cost records.
            // Pattern follows audit_logs (own PK LogId; no UpdatedAt).
            migrationBuilder.CreateTable(
                name: "ai_request_logs",
                columns: table => new
                {
                    LogId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    InputTokens = table.Column<int>(type: "integer", nullable: false),
                    OutputTokens = table.Column<int>(type: "integer", nullable: false),
                    EstimatedCost = table.Column<decimal>(type: "numeric(12,6)", nullable: false),
                    CostSource = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_request_logs", x => x.LogId);
                });

            // ── Table: ai_cost_budget_configs (US_071 TASK_001, AC-2) ────────────────────────
            // Per-provider budget thresholds and rate card pricing.
            // One row per provider — unique constraint on Provider column.
            migrationBuilder.CreateTable(
                name: "ai_cost_budget_configs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DailyBudgetThreshold = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    AlertEnabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CostPer1kInputTokens = table.Column<decimal>(type: "numeric(10,6)", nullable: false),
                    CostPer1kOutputTokens = table.Column<decimal>(type: "numeric(10,6)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_cost_budget_configs", x => x.Id);
                });

            // ── Table: ai_cost_daily_summaries (US_071 TASK_001, AC-1) ───────────────────────
            // Pre-aggregated daily cost rollup by (provider, request_type).
            // Unique composite constraint enables safe INSERT … ON CONFLICT DO UPDATE upserts.
            migrationBuilder.CreateTable(
                name: "ai_cost_daily_summaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SummaryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TotalInputTokens = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    TotalOutputTokens = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    TotalEstimatedCost = table.Column<decimal>(type: "numeric(12,6)", nullable: false, defaultValue: 0m),
                    RequestCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    ApproximateRequestCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_cost_daily_summaries", x => x.Id);
                });

            // ── Indexes: ai_request_logs ──────────────────────────────────────────────────────

            // Primary access path: time-range scans by the daily aggregation job (DESC order
            // matches ORDER BY CreatedAt DESC used by all cost aggregation queries).
            migrationBuilder.CreateIndex(
                name: "ix_ai_request_logs_created_at",
                table: "ai_request_logs",
                column: "CreatedAt",
                descending: new[] { true });

            // Per-provider aggregation: WHERE Provider = @p AND CreatedAt >= @start
            migrationBuilder.CreateIndex(
                name: "ix_ai_request_logs_provider_created_at",
                table: "ai_request_logs",
                columns: new[] { "Provider", "CreatedAt" });

            // Cross-service trace lookup: WHERE CorrelationId = @id (TR-028)
            migrationBuilder.CreateIndex(
                name: "ix_ai_request_logs_correlation_id",
                table: "ai_request_logs",
                column: "CorrelationId");

            // ── Indexes: ai_cost_budget_configs ───────────────────────────────────────────────

            // Unique provider index — O(1) lookup; enforces one row per provider.
            migrationBuilder.CreateIndex(
                name: "ix_ai_cost_budget_configs_provider",
                table: "ai_cost_budget_configs",
                column: "Provider",
                unique: true);

            // ── Indexes: ai_cost_daily_summaries ─────────────────────────────────────────────

            // Unique composite — prevents duplicate aggregation rows; supports safe upserts.
            migrationBuilder.CreateIndex(
                name: "ix_ai_cost_daily_summaries_date_provider_type",
                table: "ai_cost_daily_summaries",
                columns: new[] { "SummaryDate", "Provider", "RequestType" },
                unique: true);

            // Date-range scan: WHERE SummaryDate BETWEEN @start AND @end (dashboard queries).
            migrationBuilder.CreateIndex(
                name: "ix_ai_cost_daily_summaries_date",
                table: "ai_cost_daily_summaries",
                column: "SummaryDate",
                descending: new[] { true });

            // ── Seed: ai_cost_budget_configs ──────────────────────────────────────────────────
            // Default rate cards and daily budget thresholds for both AI providers.
            // alert_enabled = true for both — admins are notified on threshold breach (AC-2).
            migrationBuilder.InsertData(
                table: "ai_cost_budget_configs",
                columns: new[]
                {
                    "Id", "Provider", "DailyBudgetThreshold", "AlertEnabled",
                    "CostPer1kInputTokens", "CostPer1kOutputTokens",
                    "CreatedAt", "UpdatedAt"
                },
                values: new object[,]
                {
                    {
                        // OpenAI GPT-4o-mini: $0.15/1M input ($0.000150/1K), $0.60/1M output ($0.000600/1K)
                        new Guid("e0f1a2b3-c4d5-6789-abcd-ef0123456701"),
                        "OpenAI",
                        5.00m,
                        true,
                        0.000150m,
                        0.000600m,
                        new DateTime(2026, 4, 27, 0, 0, 0, DateTimeKind.Utc),
                        new DateTime(2026, 4, 27, 0, 0, 0, DateTimeKind.Utc)
                    },
                    {
                        // Anthropic Claude 3.5 Sonnet: $3.00/1M input ($0.003000/1K), $15.00/1M output ($0.015000/1K)
                        new Guid("e0f1a2b3-c4d5-6789-abcd-ef0123456702"),
                        "Anthropic",
                        20.00m,
                        true,
                        0.003000m,
                        0.015000m,
                        new DateTime(2026, 4, 27, 0, 0, 0, DateTimeKind.Utc),
                        new DateTime(2026, 4, 27, 0, 0, 0, DateTimeKind.Utc)
                    }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop in reverse order to respect potential future FK dependencies.
            migrationBuilder.DropTable(name: "ai_cost_daily_summaries");
            migrationBuilder.DropTable(name: "ai_cost_budget_configs");
            migrationBuilder.DropTable(name: "ai_request_logs");
        }
    }
}
