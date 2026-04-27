using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <summary>
    /// Extends the <c>queue_entries</c> table to support the full arrival status lifecycle
    /// introduced by US_052 (AC-1, AC-2, AC-3) and the edge-case no-show override flow.
    ///
    /// Schema additions:
    ///   - <c>CancelledAt</c>          TIMESTAMPTZ  NULL  — set when status transitions to Cancelled (AC-3)
    ///   - <c>OverrideReason</c>       TEXT         NULL  — staff reason for no-show → arrived-late override
    ///   - <c>OverriddenByUserId</c>   UUID         NULL  — FK to ApplicationUser who performed override
    ///   - <c>Version</c>              INTEGER      NOT NULL DEFAULT 0 — optimistic-concurrency token (TR-015)
    ///
    /// Indexes added:
    ///   - <c>IX_queue_entries_status_created_at</c>        btree (Status, CreatedAt) — daily queue sort (NFR-004)
    ///   - <c>IX_queue_entries_appointment_id_active</c>    UNIQUE partial on (AppointmentId)
    ///                                                       WHERE Status IN ('Waiting','ArrivedLate')
    ///                                                       — duplicate arrival guard at DB level
    ///
    /// CHECK constraints added:
    ///   - <c>CK_queue_entries_arrival_timestamp_required</c>  ArrivalTimestamp NOT NULL when active
    ///   - <c>CK_queue_entries_cancelled_at_required</c>        CancelledAt NOT NULL when Cancelled
    ///   - <c>CK_queue_entries_override_reason_max_len</c>      OverrideReason ≤ 500 chars
    ///
    /// View created:
    ///   - <c>vw_daily_queue</c>   Joins queue_entries + appointments + patients for today's queue;
    ///                             used by GET /api/queue/today to reduce query complexity (NFR-004).
    ///
    /// Rollback:
    ///   - Drops view, indexes, constraints, and new columns.
    ///   - NOTE: The enum values (NoShow, ArrivedLate, Cancelled) stored as VARCHAR(15) are
    ///     already supported by the existing column type — no type change was needed or reversed.
    ///
    /// Existing-data compatibility:
    ///   - All new columns are nullable or have defaults; existing rows are unaffected.
    ///   - The Version column defaults to 0 so all existing rows receive a valid concurrency token.
    /// </summary>
    public partial class AddArrivalStatusSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── 1. New columns ────────────────────────────────────────────────

            // CancelledAt: set when status transitions to Cancelled (AC-3).
            migrationBuilder.AddColumn<DateTime>(
                name:     "CancelledAt",
                table:    "queue_entries",
                type:     "timestamp with time zone",
                nullable: true);

            // OverrideReason: staff justification when overriding no-show to arrived-late.
            // Capped to 500 chars — enforced at service layer (FluentValidation) and DB level (CHECK below).
            migrationBuilder.AddColumn<string>(
                name:     "OverrideReason",
                table:    "queue_entries",
                type:     "text",
                nullable: true);

            // OverriddenByUserId: audit identity of the staff member who performed the override (TR-028).
            migrationBuilder.AddColumn<Guid>(
                name:     "OverriddenByUserId",
                table:    "queue_entries",
                type:     "uuid",
                nullable: true);

            // Version: optimistic-concurrency token; incremented on each UPDATE.
            // EF Core issues WHERE "Version" = @p1 on updates — stale writes throw DbUpdateConcurrencyException (TR-015).
            migrationBuilder.AddColumn<int>(
                name:         "Version",
                table:        "queue_entries",
                type:         "integer",
                nullable:     false,
                defaultValue: 0);

            // ── 2. CHECK constraints ──────────────────────────────────────────

            // Guard: ArrivalTimestamp must be set for active queue states.
            // The column is already NOT NULL at DB level; this constraint documents the invariant
            // and prevents future schema relaxation from silently violating business rules.
            migrationBuilder.Sql(
                "ALTER TABLE queue_entries " +
                "ADD CONSTRAINT \"CK_queue_entries_arrival_timestamp_required\" " +
                "CHECK (\"Status\" NOT IN ('Waiting', 'ArrivedLate') OR \"ArrivalTimestamp\" IS NOT NULL);");

            // Guard: CancelledAt must be populated when status is Cancelled (AC-3).
            migrationBuilder.Sql(
                "ALTER TABLE queue_entries " +
                "ADD CONSTRAINT \"CK_queue_entries_cancelled_at_required\" " +
                "CHECK (\"Status\" != 'Cancelled' OR \"CancelledAt\" IS NOT NULL);");

            // Guard: OverrideReason length cap (complementary to service-layer FluentValidation).
            migrationBuilder.Sql(
                "ALTER TABLE queue_entries " +
                "ADD CONSTRAINT \"CK_queue_entries_override_reason_max_len\" " +
                "CHECK (\"OverrideReason\" IS NULL OR LENGTH(\"OverrideReason\") <= 500);");

            // ── 3. Performance indexes ────────────────────────────────────────

            // Composite index: efficient daily queue retrieval sorted by Status + CreatedAt (NFR-004).
            // Supports: WHERE "Status" NOT IN ('Cancelled', 'Completed') ORDER BY "CreatedAt"
            migrationBuilder.CreateIndex(
                name:    "IX_queue_entries_status_created_at",
                table:   "queue_entries",
                columns: new[] { "Status", "CreatedAt" });

            // Partial unique index: prevents duplicate active arrivals at DB level.
            // Only active statuses (Waiting, ArrivedLate) are constrained — completed/cancelled/
            // no-show entries do not block re-arrival (edge case for rescheduled arrivals).
            // Using raw SQL because EF Core's CreateIndex does not support partial index filters
            // portably across all migration providers.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_queue_entries_appointment_id_active\" " +
                "ON queue_entries (\"AppointmentId\") " +
                "WHERE \"Status\" IN ('Waiting', 'ArrivedLate');");

            // ── 4. Daily queue view ───────────────────────────────────────────

            // vw_daily_queue: pre-joined view used by GET /api/queue/today to reduce
            // per-request join cost (NFR-004, DR-008). Patient full name is surfaced directly
            // to avoid a second round-trip. Filtered to CURRENT_DATE using session timezone.
            migrationBuilder.Sql(@"
CREATE OR REPLACE VIEW vw_daily_queue AS
SELECT
    q.""Id""                   AS queue_id,
    q.""AppointmentId""        AS appointment_id,
    p.""FullName""             AS patient_name,
    a.""AppointmentTime""      AS appointment_time,
    q.""ArrivalTimestamp""     AS arrival_timestamp,
    q.""WaitTimeMinutes""      AS wait_time_minutes,
    q.""Priority""             AS priority,
    q.""Status""               AS status,
    a.""Status""               AS appointment_status,
    q.""OverrideReason""       AS override_reason,
    q.""CancelledAt""          AS cancelled_at
FROM queue_entries q
JOIN appointments a ON q.""AppointmentId"" = a.""Id""
JOIN patients p     ON a.""PatientId""     = p.""Id""
WHERE a.""AppointmentTime""::date = CURRENT_DATE
  AND p.""DeletedAt"" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ── 1. Drop view first (depends on table columns) ─────────────────
            migrationBuilder.Sql("DROP VIEW IF EXISTS vw_daily_queue;");

            // ── 2. Drop partial unique index ──────────────────────────────────
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS \"IX_queue_entries_appointment_id_active\";");

            // ── 3. Drop composite index ───────────────────────────────────────
            migrationBuilder.DropIndex(
                name:  "IX_queue_entries_status_created_at",
                table: "queue_entries");

            // ── 4. Drop CHECK constraints ─────────────────────────────────────
            migrationBuilder.Sql(
                "ALTER TABLE queue_entries " +
                "DROP CONSTRAINT IF EXISTS \"CK_queue_entries_override_reason_max_len\";");

            migrationBuilder.Sql(
                "ALTER TABLE queue_entries " +
                "DROP CONSTRAINT IF EXISTS \"CK_queue_entries_cancelled_at_required\";");

            migrationBuilder.Sql(
                "ALTER TABLE queue_entries " +
                "DROP CONSTRAINT IF EXISTS \"CK_queue_entries_arrival_timestamp_required\";");

            // ── 5. Drop new columns ───────────────────────────────────────────
            migrationBuilder.DropColumn(
                name:  "Version",
                table: "queue_entries");

            migrationBuilder.DropColumn(
                name:  "OverriddenByUserId",
                table: "queue_entries");

            migrationBuilder.DropColumn(
                name:  "OverrideReason",
                table: "queue_entries");

            migrationBuilder.DropColumn(
                name:  "CancelledAt",
                table: "queue_entries");

            // NOTE: The Status column remains VARCHAR(15) and already accommodated the
            // new enum value strings (NoShow, ArrivedLate, Cancelled). No type change
            // was made during Up(), so no type reversion is required here.
            // The new enum values in the C# QueueStatus enum are backward-compatible
            // string values that existing VARCHAR(15) rows can represent.
        }
    }
}
