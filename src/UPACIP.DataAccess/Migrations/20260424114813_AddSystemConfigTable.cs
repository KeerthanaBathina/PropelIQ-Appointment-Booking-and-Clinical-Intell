using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemConfigTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_appointments_appointment_time",
                table: "appointments");

            migrationBuilder.CreateTable(
                name: "system_configs",
                columns: table => new
                {
                    ConfigId = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfigKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ConfigValue = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_system_configs", x => x.ConfigId);
                    table.ForeignKey(
                        name: "FK_system_configs_asp_net_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "system_configs",
                columns: new[] { "ConfigId", "ConfigKey", "ConfigValue", "CreatedAt", "Description", "UpdatedAt", "UpdatedByUserId" },
                values: new object[] { new Guid("d4e5f6a7-b8c9-0d1e-2f3a-4b5c6d7e8f90"), "queue.wait_threshold_minutes", "30", new DateTime(2026, 4, 24, 0, 0, 0, 0, DateTimeKind.Utc), "Wait time threshold in minutes for staff alerts (default: 30). Valid range: 5–120. Updated via PUT /api/queue/config/threshold.", new DateTime(2026, 4, 24, 0, 0, 0, 0, DateTimeKind.Utc), null });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_scheduled_appointment_time",
                table: "appointments",
                column: "AppointmentTime",
                filter: "\"status\" = 'Scheduled'");

            migrationBuilder.CreateIndex(
                name: "ix_system_configs_config_key",
                table: "system_configs",
                column: "ConfigKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_system_configs_UpdatedByUserId",
                table: "system_configs",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "system_configs");

            migrationBuilder.DropIndex(
                name: "ix_appointments_scheduled_appointment_time",
                table: "appointments");

            migrationBuilder.CreateIndex(
                name: "ix_appointments_appointment_time",
                table: "appointments",
                column: "AppointmentTime");
        }
    }
}
