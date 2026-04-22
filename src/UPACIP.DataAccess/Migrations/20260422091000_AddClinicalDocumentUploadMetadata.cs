using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddClinicalDocumentUploadMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ContentType — nullable for backward compatibility with pre-US_038 rows (EC-2).
            // varchar(127) is sufficient for all standard MIME type strings.
            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "clinical_documents",
                type: "character varying(127)",
                maxLength: 127,
                nullable: true);

            // FileSizeBytes — nullable for backward compatibility.
            // Persists original (pre-encryption) file size for storage auditing and UI display.
            migrationBuilder.AddColumn<long>(
                name: "FileSizeBytes",
                table: "clinical_documents",
                type: "bigint",
                nullable: true);

            // Composite (patient_id, processing_status) index — supports patient document list
            // filtered by status (US_038 EC-2; enables uploaded → queued transition queries).
            migrationBuilder.CreateIndex(
                name: "ix_clinical_documents_patient_id_status",
                table: "clinical_documents",
                columns: new[] { "PatientId", "ProcessingStatus" });

            // UploadDate index — supports temporal queries (recent documents, audit sweeps).
            migrationBuilder.CreateIndex(
                name: "ix_clinical_documents_upload_date",
                table: "clinical_documents",
                column: "UploadDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_clinical_documents_upload_date",
                table: "clinical_documents");

            migrationBuilder.DropIndex(
                name: "ix_clinical_documents_patient_id_status",
                table: "clinical_documents");

            migrationBuilder.DropColumn(
                name: "FileSizeBytes",
                table: "clinical_documents");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "clinical_documents");
        }
    }
}
