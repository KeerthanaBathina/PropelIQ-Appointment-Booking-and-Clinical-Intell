using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddCodeVerificationAndCodingAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDeprecated",
                table: "medical_codes",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OriginalCodeValue",
                table: "medical_codes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OverrideJustification",
                table: "medical_codes",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationStatus",
                table: "medical_codes",
                type: "character varying(15)",
                maxLength: 15,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<DateTime>(
                name: "VerifiedAt",
                table: "medical_codes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VerifiedByUserId",
                table: "medical_codes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "coding_audit_log",
                columns: table => new
                {
                    LogId = table.Column<Guid>(type: "uuid", nullable: false),
                    MedicalCodeId = table.Column<Guid>(type: "uuid", nullable: false),
                    PatientId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false),
                    OldCodeValue = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NewCodeValue = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Justification = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_coding_audit_log", x => x.LogId);
                    table.ForeignKey(
                        name: "FK_coding_audit_log_asp_net_users_UserId",
                        column: x => x.UserId,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_coding_audit_log_medical_codes_MedicalCodeId",
                        column: x => x.MedicalCodeId,
                        principalTable: "medical_codes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_coding_audit_log_patients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_medical_codes_patient_verification_status",
                table: "medical_codes",
                columns: new[] { "PatientId", "VerificationStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_medical_codes_VerifiedByUserId",
                table: "medical_codes",
                column: "VerifiedByUserId");

            migrationBuilder.CreateIndex(
                name: "ix_coding_audit_log_medical_code_id",
                table: "coding_audit_log",
                column: "MedicalCodeId");

            migrationBuilder.CreateIndex(
                name: "ix_coding_audit_log_patient_id_timestamp",
                table: "coding_audit_log",
                columns: new[] { "PatientId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_coding_audit_log_UserId",
                table: "coding_audit_log",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_medical_codes_asp_net_users_VerifiedByUserId",
                table: "medical_codes",
                column: "VerifiedByUserId",
                principalTable: "asp_net_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_medical_codes_asp_net_users_VerifiedByUserId",
                table: "medical_codes");

            migrationBuilder.DropTable(
                name: "coding_audit_log");

            migrationBuilder.DropIndex(
                name: "ix_medical_codes_patient_verification_status",
                table: "medical_codes");

            migrationBuilder.DropIndex(
                name: "IX_medical_codes_VerifiedByUserId",
                table: "medical_codes");

            migrationBuilder.DropColumn(
                name: "IsDeprecated",
                table: "medical_codes");

            migrationBuilder.DropColumn(
                name: "OriginalCodeValue",
                table: "medical_codes");

            migrationBuilder.DropColumn(
                name: "OverrideJustification",
                table: "medical_codes");

            migrationBuilder.DropColumn(
                name: "VerificationStatus",
                table: "medical_codes");

            migrationBuilder.DropColumn(
                name: "VerifiedAt",
                table: "medical_codes");

            migrationBuilder.DropColumn(
                name: "VerifiedByUserId",
                table: "medical_codes");
        }
    }
}
