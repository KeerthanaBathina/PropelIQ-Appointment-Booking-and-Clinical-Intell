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
                    table.CheckConstraint("ck_provider_availability_templates_end_after_start", "end_time > start_time");
                    table.CheckConstraint("ck_provider_availability_templates_slot_duration_positive", "slot_duration_minutes > 0");
                    table.ForeignKey(
                        name: "FK_provider_availability_templates_asp_net_users_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "provider_availability_templates",
                columns: new[] { "Id", "AppointmentType", "CreatedAt", "DayOfWeek", "EndTime", "IsActive", "ProviderId", "ProviderName", "SlotDurationMinutes", "StartTime", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("d1e2ec86-b5c6-7890-abcd-ef1234567893"), "General Checkup", new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc), 2, new TimeOnly(17, 0, 0), true, new Guid("d1e2f3a4-b5c6-7890-abcd-ef1234567890"), "Dr. Emily Chen", 30, new TimeOnly(8, 0, 0), new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("d1e2ec97-b5c6-7890-abcd-ef1234567894"), "General Checkup", new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc), 3, new TimeOnly(17, 0, 0), true, new Guid("d1e2f3a4-b5c6-7890-abcd-ef1234567890"), "Dr. Emily Chen", 30, new TimeOnly(8, 0, 0), new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("d1e2ecb5-b5c6-7890-abcd-ef1234567892"), "General Checkup", new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc), 1, new TimeOnly(17, 0, 0), true, new Guid("d1e2f3a4-b5c6-7890-abcd-ef1234567890"), "Dr. Emily Chen", 30, new TimeOnly(8, 0, 0), new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("d1e2ece0-b5c6-7890-abcd-ef1234567895"), "General Checkup", new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc), 4, new TimeOnly(17, 0, 0), true, new Guid("d1e2f3a4-b5c6-7890-abcd-ef1234567890"), "Dr. Emily Chen", 30, new TimeOnly(8, 0, 0), new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("d1e2ecf1-b5c6-7890-abcd-ef1234567896"), "General Checkup", new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc), 5, new TimeOnly(17, 0, 0), true, new Guid("d1e2f3a4-b5c6-7890-abcd-ef1234567890"), "Dr. Emily Chen", 30, new TimeOnly(8, 0, 0), new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("e2f39a86-c6d7-8901-bcde-f12345678904"), "Consultation", new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc), 3, new TimeOnly(16, 0, 0), true, new Guid("e2f3a4b5-c6d7-8901-bcde-f12345678901"), "Dr. Michael Park", 30, new TimeOnly(9, 0, 0), new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("e2f39a97-c6d7-8901-bcde-f12345678905"), "Consultation", new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc), 2, new TimeOnly(16, 0, 0), true, new Guid("e2f3a4b5-c6d7-8901-bcde-f12345678901"), "Dr. Michael Park", 30, new TimeOnly(9, 0, 0), new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("e2f39aa4-c6d7-8901-bcde-f12345678902"), "Consultation", new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc), 1, new TimeOnly(16, 0, 0), true, new Guid("e2f3a4b5-c6d7-8901-bcde-f12345678901"), "Dr. Michael Park", 30, new TimeOnly(9, 0, 0), new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("e2f39ae0-c6d7-8901-bcde-f12345678906"), "Consultation", new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc), 5, new TimeOnly(16, 0, 0), true, new Guid("e2f3a4b5-c6d7-8901-bcde-f12345678901"), "Dr. Michael Park", 30, new TimeOnly(9, 0, 0), new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("e2f39af1-c6d7-8901-bcde-f12345678907"), "Consultation", new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc), 4, new TimeOnly(16, 0, 0), true, new Guid("e2f3a4b5-c6d7-8901-bcde-f12345678901"), "Dr. Michael Park", 30, new TimeOnly(9, 0, 0), new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("f3a4e893-d7e8-9012-cdef-12345678901a"), "Follow-up", new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc), 5, new TimeOnly(15, 0, 0), true, new Guid("f3a4b5c6-d7e8-9012-cdef-123456789012"), "Dr. Lisa Wang", 30, new TimeOnly(10, 0, 0), new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("f3a4e8d7-d7e8-9012-cdef-123456789016"), "Follow-up", new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc), 1, new TimeOnly(15, 0, 0), true, new Guid("f3a4b5c6-d7e8-9012-cdef-123456789012"), "Dr. Lisa Wang", 30, new TimeOnly(10, 0, 0), new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("f3a4e8f5-d7e8-9012-cdef-123456789014"), "Follow-up", new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc), 3, new TimeOnly(15, 0, 0), true, new Guid("f3a4b5c6-d7e8-9012-cdef-123456789012"), "Dr. Lisa Wang", 30, new TimeOnly(10, 0, 0), new DateTime(2026, 4, 20, 0, 0, 0, 0, DateTimeKind.Utc) }
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
