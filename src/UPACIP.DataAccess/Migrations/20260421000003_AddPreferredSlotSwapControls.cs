using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <summary>
    /// Adds patient-level auto-swap control fields required for US_021 AC-3.
    ///
    /// Safe defaults:
    ///   - auto_swap_enabled defaults to TRUE so all existing patients remain eligible.
    ///   - Reason, timestamp, and actor fields are nullable — only populated on staff override.
    ///
    /// The AppointmentConfiguration JSONB GIN index for preferred_slot_criteria is also added
    /// here so preferred-slot matching queries can execute without full table scans (EC-1).
    /// </summary>
    public partial class AddPreferredSlotSwapControls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Patient auto-swap control fields (US_021, AC-3) ───────────────
            migrationBuilder.AddColumn<bool>(
                name:         "auto_swap_enabled",
                table:        "patients",
                type:         "boolean",
                nullable:     false,
                defaultValue: true);         // Safe default: all existing patients eligible

            migrationBuilder.AddColumn<string>(
                name:         "auto_swap_disabled_reason",
                table:        "patients",
                type:         "character varying(500)",
                maxLength:    500,
                nullable:     true);

            migrationBuilder.AddColumn<DateTime>(
                name:         "auto_swap_disabled_at_utc",
                table:        "patients",
                type:         "timestamp with time zone",
                nullable:     true);

            migrationBuilder.AddColumn<Guid>(
                name:         "auto_swap_disabled_by_user_id",
                table:        "patients",
                type:         "uuid",
                nullable:     true);

            // ── GIN index on preferred_slot_criteria JSONB (EC-1, DR-002) ─────
            // Enables efficient containment queries (@>) over the JSONB column so the
            // swap processor can find appointments with matching preferred criteria
            // without a sequential scan of the appointments table.
            migrationBuilder.Sql(
                "CREATE INDEX ix_appointments_preferred_slot_criteria_gin " +
                "ON appointments USING GIN (preferred_slot_criteria jsonb_path_ops) " +
                "WHERE preferred_slot_criteria IS NOT NULL;");

            // ── Composite index for swap-eligibility query (EC-2) ─────────────
            // Supports: WHERE status = 'Scheduled' AND appointment_time > @now
            // Combined with QueueEntry join to exclude arrived/in-visit rows.
            migrationBuilder.CreateIndex(
                name:    "ix_appointments_status_appointment_time",
                table:   "appointments",
                columns: new[] { "status", "appointment_time" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name:  "ix_appointments_status_appointment_time",
                table: "appointments");

            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS ix_appointments_preferred_slot_criteria_gin;");

            migrationBuilder.DropColumn(name: "auto_swap_disabled_by_user_id", table: "patients");
            migrationBuilder.DropColumn(name: "auto_swap_disabled_at_utc",     table: "patients");
            migrationBuilder.DropColumn(name: "auto_swap_disabled_reason",     table: "patients");
            migrationBuilder.DropColumn(name: "auto_swap_enabled",             table: "patients");
        }
    }
}
