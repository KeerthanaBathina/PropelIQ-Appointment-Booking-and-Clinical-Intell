using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminDashboardSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── Table: system_metrics_snapshots (US_058 AC-1, AC-2) ──────────────
            // Pre-aggregated daily system metrics for the Admin Dashboard trend chart.
            // One row per UTC calendar day; unique constraint on metric_date enables
            // safe daily upserts and O(log n) point-lookups.
            migrationBuilder.CreateTable(
                name: "system_metrics_snapshots",
                columns: table => new
                {
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    metric_date = table.Column<DateOnly>(type: "date", nullable: false),
                    active_users = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    daily_appointments = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    no_show_rate = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    ai_agreement_rate = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 0m),
                    uptime_percent = table.Column<decimal>(type: "numeric(5,2)", nullable: false, defaultValue: 100m),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_metrics_snapshots", x => x.snapshot_id);
                });

            // ── Table: notification_templates (US_058 AC-3, FR-095) ──────────────
            // Persistent notification template definitions for the Admin Configuration UI.
            // Distinct from notification_logs (individual delivery attempt records).
            migrationBuilder.CreateTable(
                name: "notification_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    channel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    trigger_event = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    message_body = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_templates", x => x.id);
                });

            // ── Indexes: system_metrics_snapshots ─────────────────────────────────
            // Unique covering index on metric_date — primary access path for date-range
            // trend queries (7-day / 30-day windows) ordered by metric_date DESC (AC-2).
            migrationBuilder.CreateIndex(
                name: "ix_system_metrics_snapshots_metric_date",
                table: "system_metrics_snapshots",
                column: "metric_date",
                unique: true);

            // ── Indexes: notification_templates ───────────────────────────────────
            // Unique index on template_name — O(1) lookup by name for Admin config load.
            migrationBuilder.CreateIndex(
                name: "ix_notification_templates_template_name",
                table: "notification_templates",
                column: "template_name",
                unique: true);

            // Composite index on (channel, trigger_event) — used by the notification
            // dispatch pipeline to find the active template for a given channel/trigger.
            migrationBuilder.CreateIndex(
                name: "ix_notification_templates_channel_trigger",
                table: "notification_templates",
                columns: new[] { "channel", "trigger_event" });

            // ── Seed default notification templates ───────────────────────────────
            migrationBuilder.InsertData(
                table: "notification_templates",
                columns: new[] { "id", "template_name", "channel", "trigger_event", "message_body", "is_active", "created_at", "updated_at" },
                values: new object[,]
                {
                    {
                        new Guid("a1b2c3d4-e5f6-7890-abcd-000000000001"),
                        "Appointment Reminder",
                        "Email",
                        "Reminder24h",
                        "Dear {{PatientName}},\n\nThis is a reminder of your appointment on {{AppointmentDate}} at {{AppointmentTime}} with {{ProviderName}}.\n\nIf you need to reschedule or cancel, please contact us at least 24 hours in advance.\n\nThank you,\nThe Care Team",
                        true,
                        new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
                        new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc)
                    },
                    {
                        new Guid("b2c3d4e5-f6a7-8901-bcde-000000000002"),
                        "Cancellation Notice",
                        "SMS",
                        "AppointmentCancelled",
                        "Hi {{PatientName}}, your appointment on {{AppointmentDate}} at {{AppointmentTime}} has been cancelled. Call us to reschedule.",
                        true,
                        new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
                        new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc)
                    },
                    {
                        new Guid("c3d4e5f6-a7b8-9012-cdef-000000000003"),
                        "No-Show Alert",
                        "Email",
                        "PatientNoShow",
                        "Dear {{PatientName}},\n\nWe noticed you missed your appointment on {{AppointmentDate}} at {{AppointmentTime}}.\n\nPlease contact us to schedule a new appointment at your earliest convenience.\n\nThank you,\nThe Care Team",
                        true,
                        new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc),
                        new DateTime(2026, 4, 26, 0, 0, 0, DateTimeKind.Utc)
                    }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "notification_templates");
            migrationBuilder.DropTable(name: "system_metrics_snapshots");
        }
    }
}
