using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddPatientContactUpdateFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AutoSwapDisabledAtUtc",
                table: "patients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AutoSwapDisabledByUserId",
                table: "patients",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AutoSwapDisabledReason",
                table: "patients",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AutoSwapEnabled",
                table: "patients",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ContactUpdateRequestedAt",
                table: "patients",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ContactUpdateRequired",
                table: "patients",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "insurance_validation_records",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn);

            migrationBuilder.AddColumn<string>(
                name: "BookingReference",
                table: "appointments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsRiskEstimated",
                table: "appointments",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NoShowRiskBand",
                table: "appointments",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "NoShowRiskScore",
                table: "appointments",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresOutreach",
                table: "appointments",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RiskCalculatedAtUtc",
                table: "appointments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "waitlist_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreferredDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PreferredStartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    PreferredEndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    PreferredProviderId = table.Column<Guid>(type: "uuid", nullable: true),
                    AppointmentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ClaimToken = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    OfferedSlotId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    OfferedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClaimedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastNotifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_waitlist_entries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_waitlist_entries_patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_appointments_no_show_risk_score_status",
                table: "appointments",
                columns: new[] { "NoShowRiskScore", "Status" });

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_claim_token",
                table: "waitlist_entries",
                column: "ClaimToken",
                unique: true,
                filter: "claim_token IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_patient_id",
                table: "waitlist_entries",
                column: "PatientId");

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_preferred_date_provider",
                table: "waitlist_entries",
                columns: new[] { "PreferredDate", "PreferredProviderId" });

            migrationBuilder.CreateIndex(
                name: "ix_waitlist_entries_status",
                table: "waitlist_entries",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "waitlist_entries");

            migrationBuilder.DropIndex(
                name: "ix_appointments_no_show_risk_score_status",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "AutoSwapDisabledAtUtc",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "AutoSwapDisabledByUserId",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "AutoSwapDisabledReason",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "AutoSwapEnabled",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "ContactUpdateRequestedAt",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "ContactUpdateRequired",
                table: "patients");

            migrationBuilder.DropColumn(
                name: "BookingReference",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "IsRiskEstimated",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "NoShowRiskBand",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "NoShowRiskScore",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "RequiresOutreach",
                table: "appointments");

            migrationBuilder.DropColumn(
                name: "RiskCalculatedAtUtc",
                table: "appointments");

            migrationBuilder.AlterColumn<int>(
                name: "Id",
                table: "insurance_validation_records",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer")
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn)
                .OldAnnotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);
        }
    }
}
