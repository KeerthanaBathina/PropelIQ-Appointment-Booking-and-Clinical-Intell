using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddQueuePositionAndAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "QueuePosition",
                table: "queue_entries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "queue_audit_logs",
                columns: table => new
                {
                    AuditId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActionType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    QueueId = table.Column<Guid>(type: "uuid", nullable: false),
                    StaffUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    OriginalPosition = table.Column<int>(type: "integer", nullable: false),
                    NewPosition = table.Column<int>(type: "integer", nullable: false),
                    OriginalPriority = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    NewPriority = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_queue_audit_logs", x => x.AuditId);
                    table.ForeignKey(
                        name: "FK_queue_audit_logs_asp_net_users_StaffUserId",
                        column: x => x.StaffUserId,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_queue_audit_logs_queue_entries_QueueId",
                        column: x => x.QueueId,
                        principalTable: "queue_entries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QueueEntry_Priority_Position",
                table: "queue_entries",
                columns: new[] { "Priority", "QueuePosition" },
                descending: new[] { true, false });

            migrationBuilder.CreateIndex(
                name: "IX_queue_audit_logs_StaffUserId",
                table: "queue_audit_logs",
                column: "StaffUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueAuditLog_QueueId_CreatedAt",
                table: "queue_audit_logs",
                columns: new[] { "QueueId", "Timestamp" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "queue_audit_logs");

            migrationBuilder.DropIndex(
                name: "IX_QueueEntry_Priority_Position",
                table: "queue_entries");

            migrationBuilder.DropColumn(
                name: "QueuePosition",
                table: "queue_entries");
        }
    }
}
