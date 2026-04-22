using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddReminderCheckpointAndStatusSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "notification_logs",
                type: "character varying(25)",
                maxLength: 25,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(10)",
                oldMaxLength: 10);

            migrationBuilder.CreateTable(
                name: "reminder_batch_checkpoints",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BatchType = table.Column<int>(type: "integer", nullable: false),
                    WindowDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WindowStartUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WindowEndUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastProcessedAppointmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastProcessedAppointmentTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessedCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    SkippedCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    FailedCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    RunStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RunId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reminder_batch_checkpoints", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reminder_batch_checkpoints_created_at",
                table: "reminder_batch_checkpoints",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "ix_reminder_batch_checkpoints_type_status_updated",
                table: "reminder_batch_checkpoints",
                columns: new[] { "BatchType", "RunStatus", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "uq_reminder_batch_checkpoints_type_window",
                table: "reminder_batch_checkpoints",
                columns: new[] { "BatchType", "WindowDateUtc" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reminder_batch_checkpoints");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "notification_logs",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(25)",
                oldMaxLength: 25);
        }
    }
}
