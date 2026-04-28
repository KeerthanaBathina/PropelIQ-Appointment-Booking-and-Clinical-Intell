using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoringTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "outage_records",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamptz", nullable: true),
                    AffectedServices = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false, defaultValue: ""),
                    ImpactLevel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Minor"),
                    AlertSentAt = table.Column<DateTime>(type: "timestamptz", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outage_records", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "uptime_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamptz", nullable: false),
                    IsHealthy = table.Column<bool>(type: "boolean", nullable: false),
                    DependencyStatusesJson = table.Column<string>(type: "text", nullable: false, defaultValue: "{}"),
                    IsMaintenanceWindow = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_uptime_snapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outage_records_started_at",
                table: "outage_records",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "ix_uptime_snapshots_timestamp",
                table: "uptime_snapshots",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "outage_records");

            migrationBuilder.DropTable(
                name: "uptime_snapshots");
        }
    }
}
