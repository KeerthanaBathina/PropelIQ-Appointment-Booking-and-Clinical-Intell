using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddSlotTemplateBusinessHoursSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Table: slot_templates (US_059 AC-1, AC-2) ────────────────────────
            // Header record for admin-configurable weekly slot templates.
            // Composite unique on (ProviderId, DayOfWeek): one template per provider/day.
            // Version is an optimistic-concurrency token (prevents lost-update anomalies).
            migrationBuilder.CreateTable(
                name: "slot_templates",
                columns: table => new
                {
                    SlotTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_slot_templates", x => x.SlotTemplateId);
                    table.ForeignKey(
                        name: "FK_slot_templates_asp_net_users_ProviderId",
                        column: x => x.ProviderId,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            // ── Table: slot_template_blocks (US_059 AC-1) ────────────────────────
            // Child time blocks within a SlotTemplate defining appointment types.
            // Cascade-deleted when parent SlotTemplate is removed.
            // Check constraint enforces EndTime > StartTime.
            migrationBuilder.CreateTable(
                name: "slot_template_blocks",
                columns: table => new
                {
                    BlockId = table.Column<Guid>(type: "uuid", nullable: false),
                    SlotTemplateId = table.Column<Guid>(type: "uuid", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    AppointmentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IsAvailable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_slot_template_blocks", x => x.BlockId);
                    table.CheckConstraint("ck_slot_template_blocks_end_after_start", "\"EndTime\" > \"StartTime\"");
                    table.ForeignKey(
                        name: "FK_slot_template_blocks_slot_templates_SlotTemplateId",
                        column: x => x.SlotTemplateId,
                        principalTable: "slot_templates",
                        principalColumn: "SlotTemplateId",
                        onDelete: ReferentialAction.Cascade);
                });

            // ── Table: business_hours (US_059 AC-3) ──────────────────────────────
            // Clinic-wide business hours — one row per day of the week (7 rows).
            // Unique on DayOfWeek. Check constraint validates open/close times when open.
            migrationBuilder.CreateTable(
                name: "business_hours",
                columns: table => new
                {
                    BusinessHoursId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    OpenTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    CloseTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    IsClosed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_hours", x => x.BusinessHoursId);
                    table.CheckConstraint(
                        "ck_business_hours_open_close_valid",
                        "\"IsClosed\" = true OR (\"OpenTime\" IS NOT NULL AND \"CloseTime\" IS NOT NULL AND \"OpenTime\" < \"CloseTime\")");
                    table.ForeignKey(
                        name: "FK_business_hours_asp_net_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // ── Table: holidays (US_059 AC-4) ─────────────────────────────────────
            // Holiday definitions with soft-delete support.
            // Partial unique index on Date WHERE DeletedAt IS NULL prevents duplicates.
            migrationBuilder.CreateTable(
                name: "holidays",
                columns: table => new
                {
                    HolidayId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsRecurring = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsHalfDay = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_holidays", x => x.HolidayId);
                    table.ForeignKey(
                        name: "FK_holidays_asp_net_users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            // ── Add nullable SlotTemplateId FK to appointments (US_059 AC-2) ─────
            // Traceability: records which slot template the appointment was booked against.
            // ON DELETE SET NULL: deleting a template does not cascade-delete appointments.
            migrationBuilder.AddColumn<Guid>(
                name: "SlotTemplateId",
                table: "appointments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_appointments_slot_templates_SlotTemplateId",
                table: "appointments",
                column: "SlotTemplateId",
                principalTable: "slot_templates",
                principalColumn: "SlotTemplateId",
                onDelete: ReferentialAction.SetNull);

            // ── Indexes: slot_templates ───────────────────────────────────────────
            // Composite unique index enforcing one template per provider/day combination.
            migrationBuilder.CreateIndex(
                name: "ix_slot_templates_provider_id_day_of_week",
                table: "slot_templates",
                columns: new[] { "ProviderId", "DayOfWeek" },
                unique: true);

            // Supports list-by-provider queries in the Admin UI.
            migrationBuilder.CreateIndex(
                name: "ix_slot_templates_provider_id",
                table: "slot_templates",
                column: "ProviderId");

            // ── Indexes: slot_template_blocks ─────────────────────────────────────
            migrationBuilder.CreateIndex(
                name: "ix_slot_template_blocks_slot_template_id",
                table: "slot_template_blocks",
                column: "SlotTemplateId");

            // ── Indexes: business_hours ───────────────────────────────────────────
            // Unique index: one row per day of week (0-6).
            migrationBuilder.CreateIndex(
                name: "ix_business_hours_day_of_week",
                table: "business_hours",
                column: "DayOfWeek",
                unique: true);

            // ── Indexes: holidays ─────────────────────────────────────────────────
            // Partial unique index: active holidays only (WHERE DeletedAt IS NULL).
            migrationBuilder.CreateIndex(
                name: "ix_holidays_date_active",
                table: "holidays",
                column: "Date",
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            // Full index on Date for booking-validation queries (including soft-deleted rows).
            migrationBuilder.CreateIndex(
                name: "ix_holidays_date",
                table: "holidays",
                column: "Date");

            // ── Index on appointments.SlotTemplateId ─────────────────────────────
            migrationBuilder.CreateIndex(
                name: "ix_appointments_slot_template_id",
                table: "appointments",
                column: "SlotTemplateId");

            // ── Seed default business hours (Mon–Fri 08:00–17:00, Sat 09:00–13:00, Sun closed) ──
            migrationBuilder.InsertData(
                table: "business_hours",
                columns: new[] { "BusinessHoursId", "DayOfWeek", "OpenTime", "CloseTime", "IsClosed", "UpdatedAt", "UpdatedByUserId" },
                values: new object[,]
                {
                    // Sunday (0) — closed
                    { new Guid("b0590001-0059-0001-0000-000000000000"), 0, null, null, true, new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc), null },
                    // Monday (1)
                    { new Guid("b0590002-0059-0001-0000-000000000000"), 1, new TimeOnly(8, 0), new TimeOnly(17, 0), false, new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc), null },
                    // Tuesday (2)
                    { new Guid("b0590003-0059-0001-0000-000000000000"), 2, new TimeOnly(8, 0), new TimeOnly(17, 0), false, new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc), null },
                    // Wednesday (3)
                    { new Guid("b0590004-0059-0001-0000-000000000000"), 3, new TimeOnly(8, 0), new TimeOnly(17, 0), false, new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc), null },
                    // Thursday (4)
                    { new Guid("b0590005-0059-0001-0000-000000000000"), 4, new TimeOnly(8, 0), new TimeOnly(17, 0), false, new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc), null },
                    // Friday (5)
                    { new Guid("b0590006-0059-0001-0000-000000000000"), 5, new TimeOnly(8, 0), new TimeOnly(17, 0), false, new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc), null },
                    // Saturday (6) — half day
                    { new Guid("b0590007-0059-0001-0000-000000000000"), 6, new TimeOnly(9, 0), new TimeOnly(13, 0), false, new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc), null },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove FK from appointments before dropping slot_templates.
            migrationBuilder.DropForeignKey(
                name: "FK_appointments_slot_templates_SlotTemplateId",
                table: "appointments");

            migrationBuilder.DropIndex(
                name: "ix_appointments_slot_template_id",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "SlotTemplateId",
                table: "appointments");

            // Drop child table before parent (FK dependency).
            migrationBuilder.DropTable(name: "slot_template_blocks");
            migrationBuilder.DropTable(name: "slot_templates");
            migrationBuilder.DropTable(name: "business_hours");
            migrationBuilder.DropTable(name: "holidays");
        }
    }
}
