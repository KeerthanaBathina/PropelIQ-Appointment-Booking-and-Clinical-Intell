using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddAgreementRateAndCodingDiscrepancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agreement_rate_metrics",
                columns: table => new
                {
                    MetricId = table.Column<Guid>(type: "uuid", nullable: false),
                    CalculationDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DailyAgreementRate = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                    Rolling30DayRate = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    TotalCodesVerified = table.Column<int>(type: "integer", nullable: false),
                    CodesApprovedWithoutOverride = table.Column<int>(type: "integer", nullable: false),
                    CodesOverridden = table.Column<int>(type: "integer", nullable: false),
                    CodesPartiallyOverridden = table.Column<int>(type: "integer", nullable: false),
                    MeetsMinimumThreshold = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_agreement_rate_metrics", x => x.MetricId);
                });

            migrationBuilder.CreateTable(
                name: "coding_discrepancies",
                columns: table => new
                {
                    DiscrepancyId = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicalCodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    AiSuggestedCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StaffSelectedCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CodeType = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    DiscrepancyType = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OverrideJustification = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    DetectedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coding_discrepancies", x => x.DiscrepancyId);
                    table.ForeignKey(
                        name: "FK_coding_discrepancies_medical_codes_MedicalCodeId",
                        column: x => x.MedicalCodeId,
                        principalTable: "medical_codes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_coding_discrepancies_patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agreement_rate_metrics_calculation_date",
                table: "agreement_rate_metrics",
                column: "CalculationDate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_coding_discrepancies_medical_code_id",
                table: "coding_discrepancies",
                column: "MedicalCodeId");

            migrationBuilder.CreateIndex(
                name: "ix_coding_discrepancies_patient_id_detected_at",
                table: "coding_discrepancies",
                columns: new[] { "PatientId", "DetectedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agreement_rate_metrics");

            migrationBuilder.DropTable(
                name: "coding_discrepancies");
        }
    }
}
