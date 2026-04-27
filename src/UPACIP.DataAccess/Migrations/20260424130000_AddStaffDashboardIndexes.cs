using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffDashboardIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Partial filtered index on queue_entries (US_057 AC-4) ─────────────
            // Supports the dashboard active-queue count query:
            //   WHERE status IN ('Waiting', 'InVisit') AND arrival_timestamp >= @todayStart
            // Covering only the two active statuses (~30 % of rows) keeps the index compact
            // and lets PostgreSQL skip all Completed / Cancelled / NoShow entries entirely.
            migrationBuilder.CreateIndex(
                name: "ix_queue_entries_status_arrival_timestamp",
                table: "queue_entries",
                columns: new[] { "status", "arrival_timestamp" },
                filter: "status IN ('Waiting', 'InVisit')");

            // ── Partial filtered index on medical_codes (US_057 AC-1) ─────────────
            // Supports the dashboard pending code-approval count and task-list query:
            //   WHERE suggested_by_ai = TRUE AND approved_by_user_id IS NULL
            // Restricting to only the pending-approval subset (< 10 % in steady state) enables
            // fast index-only scans for the staff dashboard task panel.
            migrationBuilder.CreateIndex(
                name: "ix_medical_codes_ai_pending_approval",
                table: "medical_codes",
                columns: new[] { "suggested_by_ai", "approved_by_user_id" },
                filter: "suggested_by_ai = true AND approved_by_user_id IS NULL");

            // ── Partial filtered index on extracted_data (US_057 AC-1) ───────────
            // Supports the dashboard pending document-review count and task-list query:
            //   WHERE flagged_for_review = TRUE AND verified_by_user_id IS NULL
            // Combining both equality conditions in a partial index avoids heap fetches for
            // already-verified rows, keeping the index small and highly selective.
            migrationBuilder.CreateIndex(
                name: "ix_extracted_data_pending_review",
                table: "extracted_data",
                columns: new[] { "flagged_for_review", "verified_by_user_id" },
                filter: "flagged_for_review = true AND verified_by_user_id IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_queue_entries_status_arrival_timestamp",
                table: "queue_entries");

            migrationBuilder.DropIndex(
                name: "ix_medical_codes_ai_pending_approval",
                table: "medical_codes");

            migrationBuilder.DropIndex(
                name: "ix_extracted_data_pending_review",
                table: "extracted_data");
        }
    }
}
