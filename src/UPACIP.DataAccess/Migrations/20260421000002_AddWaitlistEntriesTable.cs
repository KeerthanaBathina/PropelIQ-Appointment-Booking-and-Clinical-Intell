using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddWaitlistEntriesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "waitlist_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    preferred_date = table.Column<DateOnly>(type: "date", nullable: false),
                    preferred_start_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    preferred_end_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    preferred_provider_id = table.Column<Guid>(type: "uuid", nullable: true),
                    appointment_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    claim_token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    offered_slot_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    offered_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    claim_expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    claimed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_notified_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_waitlist_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_waitlist_entries_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Indexes
            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_patient_id",
                table: "waitlist_entries",
                column: "patient_id");

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_status",
                table: "waitlist_entries",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_preferred_date_provider",
                table: "waitlist_entries",
                columns: new[] { "preferred_date", "preferred_provider_id" });

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_claim_token",
                table: "waitlist_entries",
                column: "claim_token",
                unique: true,
                filter: "claim_token IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "waitlist_entries");
        }
    }
}
