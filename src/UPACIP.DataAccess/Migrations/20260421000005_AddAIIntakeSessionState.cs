using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <summary>
    /// Adds AI conversational intake session state columns and supporting indexes
    /// to the <c>intake_data</c> table (US_027, AC-3, AC-5, EC-1, EC-2).
    ///
    /// Schema additions:
    ///   - <c>ai_session_id</c>          UUID        NULL — links DB row to Redis session cache (EC-2)
    ///   - <c>ai_session_status</c>      VARCHAR(20) NULL — 'active' | 'summary' | 'completed' | 'manual'
    ///   - <c>last_auto_saved_at</c>     TIMESTAMPTZ NULL — UTC timestamp of most recent autosave
    ///   - <c>ai_session_snapshot</c>    JSONB       NULL — full session state snapshot (EC-2 restore)
    ///
    /// Safety / rollout considerations:
    ///   - All columns are nullable: existing intake rows are unaffected; NULL means
    ///     "manually submitted" and the application treats NULL status as non-AI intake (AC-3).
    ///   - The JSONB snapshot column stores collected fields as a key-value array so the
    ///     conversational service can rebuild its in-memory state after a Redis TTL expiry (EC-2).
    ///   - Partial indexes exclude NULL-status rows so manually submitted intake records
    ///     never appear in AI session resume queries (EC-2 correctness).
    ///
    /// Down() reverses all changes cleanly for rollback safety.
    /// </summary>
    public partial class AddAIIntakeSessionState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. Session ID — links DB row to Redis cache entry (EC-2) ─────────
            migrationBuilder.AddColumn<Guid>(
                name:     "ai_session_id",
                table:    "intake_data",
                type:     "uuid",
                nullable: true);

            // Non-unique index: fast lookup when resuming by session ID.
            // Partial: only indexes AI intake rows (ai_session_id IS NOT NULL).
            migrationBuilder.Sql(
                "CREATE INDEX ix_intake_data_ai_session_id " +
                "ON intake_data (ai_session_id) " +
                "WHERE ai_session_id IS NOT NULL;");

            // ── 2. Session status (AC-3 — 'active'|'summary'|'completed'|'manual') ─
            migrationBuilder.AddColumn<string>(
                name:      "ai_session_status",
                table:     "intake_data",
                type:      "character varying(20)",
                maxLength: 20,
                nullable:  true);

            // Composite partial index: drives the active-session resume query (EC-2).
            // Pattern: WHERE patient_id = @id AND ai_session_status = 'active'
            //          ORDER BY last_auto_saved_at DESC LIMIT 1
            // Partial: excludes manually submitted rows where ai_session_status IS NULL.
            migrationBuilder.Sql(
                "CREATE INDEX ix_intake_data_patient_ai_status " +
                "ON intake_data (patient_id, ai_session_status) " +
                "WHERE ai_session_status IS NOT NULL;");

            // ── 3. Autosave timestamp — EC-2 tiebreaker for draft row selection ─
            migrationBuilder.AddColumn<DateTime>(
                name:     "last_auto_saved_at",
                table:    "intake_data",
                type:     "timestamp with time zone",
                nullable: true);

            // ── 4. Session state snapshot (JSONB) — full EC-2 restore payload ──
            // Stores collected fields (key-value list), current question key, turn count,
            // and consecutive provider failure count so the service can rebuild its state
            // after a Redis TTL expiry without data loss (EC-1: ambiguous-term clarification
            // progress survives autosave and resume).
            migrationBuilder.AddColumn<string>(
                name:     "ai_session_snapshot",
                table:    "intake_data",
                type:     "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_intake_data_patient_ai_status;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_intake_data_ai_session_id;");

            migrationBuilder.DropColumn(name: "ai_session_snapshot", table: "intake_data");
            migrationBuilder.DropColumn(name: "last_auto_saved_at",  table: "intake_data");
            migrationBuilder.DropColumn(name: "ai_session_status",   table: "intake_data");
            migrationBuilder.DropColumn(name: "ai_session_id",       table: "intake_data");
        }
    }
}
