using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <summary>
    /// Adds no-show risk metadata columns and supporting index to the <c>appointments</c> table
    /// (US_026, AIR-006, FR-014, AC-1, AC-3, AC-4, EC-1, EC-2).
    ///
    /// Schema additions:
    ///   - <c>no_show_risk_score</c>     INTEGER     NULL — persisted 0-100 score (CHECK enforced)
    ///   - <c>no_show_risk_band</c>      VARCHAR(10) NULL — 'Low' | 'Medium' | 'High'
    ///   - <c>is_risk_estimated</c>      BOOLEAN     NULL — true when score produced by fallback rules
    ///   - <c>requires_outreach</c>      BOOLEAN     NULL — true when score >= outreach threshold
    ///   - <c>risk_calculated_at_utc</c> TIMESTAMPTZ NULL — UTC timestamp of last scoring run
    ///
    /// Safety / rollout considerations:
    ///   - All columns are nullable: existing appointment rows are unaffected; NULL means
    ///     "not yet scored" and the application handles this as an absent risk score (EC-1).
    ///   - CHECK constraint caps persisted score to [0, 100] so a buggy scoring service
    ///     cannot corrupt data (EC-2, DR guardrails).
    ///   - The composite index ix_appointments_no_show_risk_score_status serves the staff
    ///     schedule/queue sort-by-risk pattern and the slot-swap prioritization filter (AC-4).
    ///
    /// Down() reverses all changes cleanly for rollback safety (DR-029).
    /// </summary>
    public partial class AddAppointmentNoShowRiskMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Risk score column ──────────────────────────────────────────
            migrationBuilder.AddColumn<int>(
                name:         "no_show_risk_score",
                table:        "appointments",
                type:         "integer",
                nullable:     true);

            // CHECK constraint: enforces guardrail that persisted scores stay in [0, 100] (EC-2).
            // Applied after AddColumn so the constraint is a separate DDL statement that can be
            // rolled back independently if needed.
            migrationBuilder.Sql(
                "ALTER TABLE appointments " +
                "ADD CONSTRAINT ck_appointments_no_show_risk_score_range " +
                "CHECK (no_show_risk_score IS NULL OR (no_show_risk_score >= 0 AND no_show_risk_score <= 100));");

            // ── 2. Risk band column (string: 'Low' | 'Medium' | 'High') ───────
            migrationBuilder.AddColumn<string>(
                name:         "no_show_risk_band",
                table:        "appointments",
                type:         "character varying(10)",
                maxLength:    10,
                nullable:     true);

            // ── 3. Estimated-score flag ───────────────────────────────────────
            migrationBuilder.AddColumn<bool>(
                name:         "is_risk_estimated",
                table:        "appointments",
                type:         "boolean",
                nullable:     true);

            // ── 4. Outreach flag (EC-2) ───────────────────────────────────────
            migrationBuilder.AddColumn<bool>(
                name:         "requires_outreach",
                table:        "appointments",
                type:         "boolean",
                nullable:     true);

            // ── 5. Calculation timestamp ──────────────────────────────────────
            migrationBuilder.AddColumn<DateTime>(
                name:         "risk_calculated_at_utc",
                table:        "appointments",
                type:         "timestamp with time zone",
                nullable:     true);

            // ── 6. Composite index: risk-sorted staff queries (AC-4) ──────────
            // Supports: WHERE status = 'Scheduled' ORDER BY no_show_risk_score DESC
            // Also used by slot-swap engine to prioritize lower-risk patients (AC-4).
            // Partial index WHERE no_show_risk_score IS NOT NULL keeps the index compact;
            // rows not yet scored are excluded since they cannot be meaningfully sorted.
            migrationBuilder.Sql(
                "CREATE INDEX ix_appointments_no_show_risk_score_status " +
                "ON appointments (no_show_risk_score, status) " +
                "WHERE no_show_risk_score IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS ix_appointments_no_show_risk_score_status;");

            migrationBuilder.Sql(
                "ALTER TABLE appointments " +
                "DROP CONSTRAINT IF EXISTS ck_appointments_no_show_risk_score_range;");

            migrationBuilder.DropColumn(name: "risk_calculated_at_utc", table: "appointments");
            migrationBuilder.DropColumn(name: "requires_outreach",       table: "appointments");
            migrationBuilder.DropColumn(name: "is_risk_estimated",       table: "appointments");
            migrationBuilder.DropColumn(name: "no_show_risk_band",       table: "appointments");
            migrationBuilder.DropColumn(name: "no_show_risk_score",      table: "appointments");
        }
    }
}
