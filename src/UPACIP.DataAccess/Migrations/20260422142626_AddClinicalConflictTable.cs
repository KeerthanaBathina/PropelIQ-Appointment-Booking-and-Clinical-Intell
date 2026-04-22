using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicalConflictTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "clinical_conflicts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conflict_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Detected"),
                    is_urgent = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    source_extracted_data_ids = table.Column<string>(type: "jsonb", nullable: false),
                    source_document_ids = table.Column<string>(type: "jsonb", nullable: false),
                    conflict_description = table.Column<string>(type: "text", nullable: false),
                    ai_explanation = table.Column<string>(type: "text", nullable: false),
                    ai_confidence_score = table.Column<float>(type: "real", nullable: false),
                    resolved_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resolution_notes = table.Column<string>(type: "text", nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    profile_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_clinical_conflicts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_clinical_conflicts_asp_net_users_resolved_by_user_id",
                        column: x => x.resolved_by_user_id,
                        principalTable: "asp_net_users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_clinical_conflicts_patient_profile_versions_profile_version~",
                        column: x => x.profile_version_id,
                        principalTable: "patient_profile_versions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_clinical_conflicts_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_clinical_documents_PreviousVersionId",
                table: "clinical_documents",
                column: "PreviousVersionId");

            migrationBuilder.CreateIndex(
                name: "ix_clinical_conflicts_is_urgent_created_at",
                table: "clinical_conflicts",
                columns: new[] { "is_urgent", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_clinical_conflicts_patient_id_conflict_type",
                table: "clinical_conflicts",
                columns: new[] { "patient_id", "conflict_type" });

            migrationBuilder.CreateIndex(
                name: "ix_clinical_conflicts_patient_id_status",
                table: "clinical_conflicts",
                columns: new[] { "patient_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_clinical_conflicts_profile_version_id",
                table: "clinical_conflicts",
                column: "profile_version_id");

            migrationBuilder.CreateIndex(
                name: "IX_clinical_conflicts_resolved_by_user_id",
                table: "clinical_conflicts",
                column: "resolved_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "clinical_conflicts");

            migrationBuilder.DropIndex(
                name: "IX_clinical_documents_PreviousVersionId",
                table: "clinical_documents");
        }
    }
}
