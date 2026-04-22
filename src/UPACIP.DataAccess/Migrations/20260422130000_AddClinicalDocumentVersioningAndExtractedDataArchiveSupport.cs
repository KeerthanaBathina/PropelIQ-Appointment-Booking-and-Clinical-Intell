using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicalDocumentVersioningAndExtractedDataArchiveSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── ClinicalDocument: version lineage and reconsolidation signal (US_042 task_004) ──

            migrationBuilder.AddColumn<int>(
                name: "VersionNumber",
                table: "clinical_documents",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<Guid>(
                name: "PreviousVersionId",
                table: "clinical_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsSuperseded",
                table: "clinical_documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "SupersededAtUtc",
                table: "clinical_documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReconsolidationNeeded",
                table: "clinical_documents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Self-referencing FK: replacement document → its predecessor.
            migrationBuilder.AddForeignKey(
                name: "fk_clinical_documents_previous_version",
                table: "clinical_documents",
                column: "PreviousVersionId",
                principalTable: "clinical_documents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Active-version query index: find current non-superseded documents per patient.
            migrationBuilder.CreateIndex(
                name: "ix_clinical_documents_patient_id_is_superseded",
                table: "clinical_documents",
                columns: new[] { "PatientId", "IsSuperseded" });

            // Reconsolidation work-queue index.
            migrationBuilder.CreateIndex(
                name: "ix_clinical_documents_reconsolidation_needed",
                table: "clinical_documents",
                column: "ReconsolidationNeeded");

            // ── ExtractedData: archive fields for superseded rows (US_042 task_004 AC-3, EC-1) ──

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "extracted_data",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAtUtc",
                table: "extracted_data",
                type: "timestamp with time zone",
                nullable: true);

            // Active-data query index: efficient non-archived row retrieval by document.
            migrationBuilder.CreateIndex(
                name: "ix_extracted_data_document_id_is_archived",
                table: "extracted_data",
                columns: new[] { "DocumentId", "IsArchived" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_extracted_data_document_id_is_archived",
                table: "extracted_data");

            migrationBuilder.DropColumn(
                name: "ArchivedAtUtc",
                table: "extracted_data");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "extracted_data");

            migrationBuilder.DropForeignKey(
                name: "fk_clinical_documents_previous_version",
                table: "clinical_documents");

            migrationBuilder.DropIndex(
                name: "ix_clinical_documents_reconsolidation_needed",
                table: "clinical_documents");

            migrationBuilder.DropIndex(
                name: "ix_clinical_documents_patient_id_is_superseded",
                table: "clinical_documents");

            migrationBuilder.DropColumn(
                name: "ReconsolidationNeeded",
                table: "clinical_documents");

            migrationBuilder.DropColumn(
                name: "SupersededAtUtc",
                table: "clinical_documents");

            migrationBuilder.DropColumn(
                name: "IsSuperseded",
                table: "clinical_documents");

            migrationBuilder.DropColumn(
                name: "PreviousVersionId",
                table: "clinical_documents");

            migrationBuilder.DropColumn(
                name: "VersionNumber",
                table: "clinical_documents");
        }
    }
}
