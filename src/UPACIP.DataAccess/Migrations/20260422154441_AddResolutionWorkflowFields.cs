using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddResolutionWorkflowFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "verification_status",
                table: "patient_profile_versions",
                type: "character varying(25)",
                maxLength: 25,
                nullable: false,
                defaultValue: "Unverified");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "verified_at",
                table: "patient_profile_versions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "verified_by_user_id",
                table: "patient_profile_versions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "both_valid_explanation",
                table: "clinical_conflicts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "resolution_type",
                table: "clinical_conflicts",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "selected_extracted_data_id",
                table: "clinical_conflicts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_patient_profile_versions_patient_id_verification_status",
                table: "patient_profile_versions",
                columns: new[] { "patient_id", "verification_status" });

            migrationBuilder.CreateIndex(
                name: "IX_patient_profile_versions_verified_by_user_id",
                table: "patient_profile_versions",
                column: "verified_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_clinical_conflicts_selected_extracted_data_id",
                table: "clinical_conflicts",
                column: "selected_extracted_data_id");

            migrationBuilder.AddForeignKey(
                name: "FK_clinical_conflicts_extracted_data_selected_extracted_data_id",
                table: "clinical_conflicts",
                column: "selected_extracted_data_id",
                principalTable: "extracted_data",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_patient_profile_versions_asp_net_users_verified_by_user_id",
                table: "patient_profile_versions",
                column: "verified_by_user_id",
                principalTable: "asp_net_users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_clinical_conflicts_extracted_data_selected_extracted_data_id",
                table: "clinical_conflicts");

            migrationBuilder.DropForeignKey(
                name: "FK_patient_profile_versions_asp_net_users_verified_by_user_id",
                table: "patient_profile_versions");

            migrationBuilder.DropIndex(
                name: "ix_patient_profile_versions_patient_id_verification_status",
                table: "patient_profile_versions");

            migrationBuilder.DropIndex(
                name: "IX_patient_profile_versions_verified_by_user_id",
                table: "patient_profile_versions");

            migrationBuilder.DropIndex(
                name: "IX_clinical_conflicts_selected_extracted_data_id",
                table: "clinical_conflicts");

            migrationBuilder.DropColumn(
                name: "verification_status",
                table: "patient_profile_versions");

            migrationBuilder.DropColumn(
                name: "verified_at",
                table: "patient_profile_versions");

            migrationBuilder.DropColumn(
                name: "verified_by_user_id",
                table: "patient_profile_versions");

            migrationBuilder.DropColumn(
                name: "both_valid_explanation",
                table: "clinical_conflicts");

            migrationBuilder.DropColumn(
                name: "resolution_type",
                table: "clinical_conflicts");

            migrationBuilder.DropColumn(
                name: "selected_extracted_data_id",
                table: "clinical_conflicts");
        }
    }
}
