using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationDeliveryAttemptAndStatusSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FinalAttemptAt",
                table: "notification_logs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsContactValidationRequired",
                table: "notification_logs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsStaffReviewRequired",
                table: "notification_logs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RecipientAddress",
                table: "notification_logs",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "notification_delivery_attempts",
                columns: table => new
                {
                    AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationId = table.Column<Guid>(type: "uuid", nullable: false),
                    AppointmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    RecipientAddress = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    AttemptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_delivery_attempts", x => x.AttemptId);
                    table.ForeignKey(
                        name: "FK_notification_delivery_attempts_notification_logs_Notificati~",
                        column: x => x.NotificationId,
                        principalTable: "notification_logs",
                        principalColumn: "NotificationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notification_logs_created_at",
                table: "notification_logs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "ix_notification_logs_staff_review_required",
                table: "notification_logs",
                column: "IsStaffReviewRequired");

            migrationBuilder.CreateIndex(
                name: "ix_notification_logs_status",
                table: "notification_logs",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "ix_notification_delivery_attempts_appointment_id",
                table: "notification_delivery_attempts",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "ix_notification_delivery_attempts_channel",
                table: "notification_delivery_attempts",
                column: "Channel");

            migrationBuilder.CreateIndex(
                name: "ix_notification_delivery_attempts_notification_id",
                table: "notification_delivery_attempts",
                column: "NotificationId");

            migrationBuilder.CreateIndex(
                name: "ix_notification_delivery_attempts_status_attempted_at",
                table: "notification_delivery_attempts",
                columns: new[] { "Status", "AttemptedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_delivery_attempts");

            migrationBuilder.DropIndex(
                name: "ix_notification_logs_created_at",
                table: "notification_logs");

            migrationBuilder.DropIndex(
                name: "ix_notification_logs_staff_review_required",
                table: "notification_logs");

            migrationBuilder.DropIndex(
                name: "ix_notification_logs_status",
                table: "notification_logs");

            migrationBuilder.DropColumn(
                name: "FinalAttemptAt",
                table: "notification_logs");

            migrationBuilder.DropColumn(
                name: "IsContactValidationRequired",
                table: "notification_logs");

            migrationBuilder.DropColumn(
                name: "IsStaffReviewRequired",
                table: "notification_logs");

            migrationBuilder.DropColumn(
                name: "RecipientAddress",
                table: "notification_logs");
        }
    }
}
