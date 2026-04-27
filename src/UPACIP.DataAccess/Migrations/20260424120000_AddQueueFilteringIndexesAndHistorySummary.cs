using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddQueueFilteringIndexesAndHistorySummary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── New indexes on queue_entries (US_056 AC-2) ────────────────────────
            // Composite index supporting filtered queue queries that combine status,
            // priority tier, and creation time (AND logic filtering dashboard).
            migrationBuilder.CreateIndex(
                name: "ix_queue_entries_status_priority_created_at",
                table: "queue_entries",
                columns: new[] { "Status", "Priority", "CreatedAt" });

            // ── New index on appointments (US_056 AC-1) ──────────────────────────
            // Provider-first composite index for filtered queue queries:
            //   WHERE provider_id = @provider AND appointment_time >= @todayStart
            // Complements the existing (appointment_time, status, provider_id) index
            // where appointment_time leads and provider_id is the trailing column.
            migrationBuilder.CreateIndex(
                name: "ix_appointments_provider_id_appointment_time",
                table: "appointments",
                columns: new[] { "ProviderId", "AppointmentTime" });

            // ── queue_daily_summary table (US_056 AC-3, AC-4) ────────────────────
            migrationBuilder.CreateTable(
                name: "queue_daily_summary",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SummaryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppointmentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    AvgWaitTimeMinutes = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    NoShowCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CompletedCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    TotalPatients = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_queue_daily_summary", x => x.Id);
                    table.ForeignKey(
                        name: "FK_queue_daily_summary_asp_net_users_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // Date-range scan index — answers WHERE summary_date BETWEEN @start AND @end
            migrationBuilder.CreateIndex(
                name: "ix_queue_daily_summary_date",
                table: "queue_daily_summary",
                column: "SummaryDate");

            // Unique composite constraint: one row per (date, provider, appointment_type) partition.
            // Enables safe INSERT ... ON CONFLICT DO UPDATE upserts by the aggregation job.
            migrationBuilder.CreateIndex(
                name: "ix_queue_daily_summary_date_provider_type",
                table: "queue_daily_summary",
                columns: new[] { "SummaryDate", "ProviderId", "AppointmentType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "queue_daily_summary");

            migrationBuilder.DropIndex(
                name: "ix_queue_entries_status_priority_created_at",
                table: "queue_entries");

            migrationBuilder.DropIndex(
                name: "ix_appointments_provider_id_appointment_time",
                table: "appointments");
        }
    }
}
