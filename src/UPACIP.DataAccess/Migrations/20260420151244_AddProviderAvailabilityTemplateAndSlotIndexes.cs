using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderAvailabilityTemplateAndSlotIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "provider_availability_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    SlotDurationMinutes = table.Column<int>(type: "integer", nullable: false, defaultValue: 30),
                    AppointmentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false, defaultValue: "General Checkup"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_availability_templates", x => x.Id);
                    table.CheckConstraint("ck_provider_availability_templates_end_after_start", "\"EndTime\" > \"StartTime\"");
                    table.CheckConstraint("ck_provider_availability_templates_slot_duration_positive", "\"SlotDurationMinutes\" > 0");
                    table.ForeignKey(
                        name: "FK_provider_availability_templates_asp_net_users_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_appointment_time_status",
                table: "appointments",
                columns: new[] { "AppointmentTime", "Status" });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_appointment_time_status_provider_id",
                table: "appointments",
                columns: new[] { "AppointmentTime", "Status", "ProviderId" });

            migrationBuilder.CreateIndex(
                name: "ix_provider_availability_templates_provider_id",
                table: "provider_availability_templates",
                column: "ProviderId");

            migrationBuilder.CreateIndex(
                name: "ix_provider_availability_templates_provider_id_day_of_week",
                table: "provider_availability_templates",
                columns: new[] { "ProviderId", "DayOfWeek" });

            migrationBuilder.CreateIndex(
                name: "uq_provider_availability_templates_provider_day_start",
                table: "provider_availability_templates",
                columns: new[] { "ProviderId", "DayOfWeek", "StartTime" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "provider_availability_templates");

            migrationBuilder.DropIndex(
                name: "ix_appointments_appointment_time_status",
                table: "appointments");

            migrationBuilder.DropIndex(
                name: "ix_appointments_appointment_time_status_provider_id",
                table: "appointments");
        }
    }
}
