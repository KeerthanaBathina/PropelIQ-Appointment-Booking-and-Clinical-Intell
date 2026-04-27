using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationTemplateAndRiskConfigSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── ALTER TABLE: notification_templates — US_060 new columns ─────────
            // Add subject (nullable, email-only), allowed_variables (jsonb),
            // version (optimistic-concurrency token), and updated_by_user_id (FK).

            migrationBuilder.AddColumn<string>(
                name: "Subject",
                table: "notification_templates",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AllowedVariables",
                table: "notification_templates",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "notification_templates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "UpdatedByUserId",
                table: "notification_templates",
                type: "uuid",
                nullable: true);

            // FK to asp_net_users for admin attribution (US_060 AC-4)
            migrationBuilder.AddForeignKey(
                name: "FK_notification_templates_asp_net_users_UpdatedByUserId",
                table: "notification_templates",
                column: "UpdatedByUserId",
                principalTable: "asp_net_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ── CREATE TABLE: risk_configuration (US_060 AC-3, AC-4) ─────────────
            // Singleton table — exactly one row seeded below.
            // HighRiskThreshold and MediumRiskThreshold have range check constraints (0–100).
            // ScoringParameters stored as jsonb.
            // Version is an optimistic-concurrency token.
            migrationBuilder.CreateTable(
                name: "risk_configuration",
                columns: table => new
                {
                    RiskConfigId = table.Column<Guid>(type: "uuid", nullable: false),
                    HighRiskThreshold = table.Column<int>(type: "integer", nullable: false, defaultValue: 75),
                    MediumRiskThreshold = table.Column<int>(type: "integer", nullable: false, defaultValue: 45),
                    MinAppointmentsForAiScore = table.Column<int>(type: "integer", nullable: false, defaultValue: 3),
                    AutoOutreach = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    ScoringParameters = table.Column<string>(type: "jsonb", nullable: false),
                    RecalculationPending = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    LastRecalculatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_risk_configuration", x => x.RiskConfigId);
                    table.CheckConstraint(
                        "ck_risk_configuration_high_threshold_range",
                        "\"HighRiskThreshold\" >= 0 AND \"HighRiskThreshold\" <= 100");
                    table.CheckConstraint(
                        "ck_risk_configuration_medium_threshold_range",
                        "\"MediumRiskThreshold\" >= 0 AND \"MediumRiskThreshold\" <= 100");
                    table.ForeignKey(
                        name: "FK_risk_configuration_asp_net_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_risk_configuration_updated_by_user_id",
                table: "risk_configuration",
                column: "UpdatedByUserId");

            // ── Seed: 6 US_060 notification templates ────────────────────────────
            migrationBuilder.InsertData(
                table: "notification_templates",
                columns: new[] { "id", "template_name", "channel", "trigger_event", "subject", "message_body", "allowed_variables", "is_active", "Version", "UpdatedByUserId", "created_at", "updated_at" },
                values: new object[,]
                {
                    // Booking Confirmation — Email
                    {
                        new Guid("d4e5f6a7-b8c9-0123-def0-000000000004"),
                        "Booking Confirmation (Email)",
                        "Email",
                        "AppointmentBooked",
                        "Appointment Confirmed",
                        "Dear {{patient_name}},\n\nYour appointment has been confirmed for {{date}} at {{time}} with {{provider}}.\n\nPlease arrive 10 minutes early. If you need to reschedule, contact us at least 24 hours in advance.\n\nThank you,\nThe Care Team",
                        "[\"patient_name\",\"date\",\"time\",\"provider\"]",
                        true,
                        0,
                        null,
                        new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
                        new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc)
                    },
                    // Booking Confirmation — SMS
                    {
                        new Guid("e5f6a7b8-c9d0-1234-ef01-000000000005"),
                        "Booking Confirmation (SMS)",
                        "SMS",
                        "AppointmentBooked",
                        null,
                        "Hi {{patient_name}}, your appointment is confirmed for {{date}} at {{time}}. Reply CANCEL to cancel.",
                        "[\"patient_name\",\"date\",\"time\"]",
                        true,
                        0,
                        null,
                        new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
                        new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc)
                    },
                    // 24h Reminder — Email
                    {
                        new Guid("f6a7b8c9-d0e1-2345-f012-000000000006"),
                        "24h Reminder (Email)",
                        "Email",
                        "Reminder24h",
                        "Appointment Reminder \u2014 Tomorrow",
                        "Dear {{patient_name}},\n\nThis is a reminder that you have an appointment tomorrow, {{date}}, at {{time}} with {{provider}}.\n\nIf you need to cancel, please let us know at least 24 hours in advance.\n\nThank you,\nThe Care Team",
                        "[\"patient_name\",\"date\",\"time\",\"provider\"]",
                        true,
                        0,
                        null,
                        new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
                        new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc)
                    },
                    // 24h Reminder — SMS
                    {
                        new Guid("a7b8c9d0-e1f2-3456-0123-000000000007"),
                        "24h Reminder (SMS)",
                        "SMS",
                        "Reminder24h",
                        null,
                        "Reminder: {{patient_name}}, you have an appointment tomorrow {{date}} at {{time}}. Reply CANCEL to cancel.",
                        "[\"patient_name\",\"date\",\"time\"]",
                        true,
                        0,
                        null,
                        new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
                        new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc)
                    },
                    // 2h Reminder — Email
                    {
                        new Guid("b8c9d0e1-f2a3-4567-1234-000000000008"),
                        "2h Reminder (Email)",
                        "Email",
                        "Reminder2h",
                        "Appointment in 2 Hours",
                        "Dear {{patient_name}},\n\nYour appointment with {{provider}} is in 2 hours at {{time}} today, {{date}}.\n\nPlease make sure you arrive on time.\n\nThank you,\nThe Care Team",
                        "[\"patient_name\",\"date\",\"time\",\"provider\"]",
                        true,
                        0,
                        null,
                        new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
                        new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc)
                    },
                    // 2h Reminder — SMS
                    {
                        new Guid("c9d0e1f2-a3b4-5678-2345-000000000009"),
                        "2h Reminder (SMS)",
                        "SMS",
                        "Reminder2h",
                        null,
                        "{{patient_name}}, your appointment with {{provider}} is in 2 hours at {{time}}. See you soon!",
                        "[\"patient_name\",\"time\",\"provider\"]",
                        true,
                        0,
                        null,
                        new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
                        new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc)
                    }
                });

            // ── Seed: default risk configuration (singleton row) ─────────────────
            migrationBuilder.InsertData(
                table: "risk_configuration",
                columns: new[] { "RiskConfigId", "HighRiskThreshold", "MediumRiskThreshold", "MinAppointmentsForAiScore", "AutoOutreach", "ScoringParameters", "RecalculationPending", "LastRecalculatedAt", "Version", "UpdatedByUserId", "UpdatedAt" },
                values: new object[]
                {
                    new Guid("d0e1f2a3-b4c5-6789-abcd-000000000010"),
                    75,
                    45,
                    3,
                    true,
                    "{\"priorNoShowsWeight\":0.50,\"cancellationHistoryWeight\":0.30,\"appointmentLeadTimeWeight\":0.20}",
                    false,
                    null,
                    0,
                    null,
                    new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc)
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove risk_configuration table entirely
            migrationBuilder.DropTable(name: "risk_configuration");

            // Remove the FK before dropping columns
            migrationBuilder.DropForeignKey(
                name: "FK_notification_templates_asp_net_users_UpdatedByUserId",
                table: "notification_templates");

            // Delete the 6 US_060 seeded templates
            migrationBuilder.DeleteData(
                table: "notification_templates",
                keyColumn: "id",
                keyValues: new object[]
                {
                    new Guid("d4e5f6a7-b8c9-0123-def0-000000000004"),
                    new Guid("e5f6a7b8-c9d0-1234-ef01-000000000005"),
                    new Guid("f6a7b8c9-d0e1-2345-f012-000000000006"),
                    new Guid("a7b8c9d0-e1f2-3456-0123-000000000007"),
                    new Guid("b8c9d0e1-f2a3-4567-1234-000000000008"),
                    new Guid("c9d0e1f2-a3b4-5678-2345-000000000009")
                });

            // Remove the added columns
            migrationBuilder.DropColumn(name: "UpdatedByUserId", table: "notification_templates");
            migrationBuilder.DropColumn(name: "Version",          table: "notification_templates");
            migrationBuilder.DropColumn(name: "AllowedVariables", table: "notification_templates");
            migrationBuilder.DropColumn(name: "Subject",          table: "notification_templates");
        }
    }
}
