using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UPACIP.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class AddExtractedDataAttributionAndDocumentOutcomes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── ExtractedData: structured attribution columns (US_040 task_003, AC-5) ──────────────

            // PageNumber — page within the source document where the data point was found.
            // Defaults to 1 for backward compatibility with rows created before this migration.
            migrationBuilder.AddColumn<int>(
                name: "PageNumber",
                table: "extracted_data",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // ExtractionRegion — coarse region within the page (e.g. "body", "table", "header").
            // Defaults to empty string for backward compatibility.
            migrationBuilder.AddColumn<string>(
                name: "ExtractionRegion",
                table: "extracted_data",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            // ── ExtractedData: new retrieval indexes ─────────────────────────────────────────────

            // Composite index: group extracted rows by document + data type for profile assembly.
            migrationBuilder.CreateIndex(
                name: "ix_extracted_data_document_id_data_type",
                table: "extracted_data",
                columns: new[] { "DocumentId", "DataType" });

            // Temporal index: audit sweeps and time-ordered extraction retrieval.
            migrationBuilder.CreateIndex(
                name: "ix_extracted_data_created_at",
                table: "extracted_data",
                column: "CreatedAt");

            // ── ClinicalDocument: extraction outcome column (US_040 task_003, EC-1, EC-2) ─────────

            // ExtractionOutcome — dedicated column storing the AI extraction result status.
            // Possible values: 'extracted', 'no-data-extracted', 'unsupported-language',
            // 'invalid-response'. Null until clinical extraction has run.
            // Separate from ProcessingStatus so edge-case outcomes do not overload 'failed'.
            migrationBuilder.AddColumn<string>(
                name: "ExtractionOutcome",
                table: "clinical_documents",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            // ── ClinicalDocument: extraction outcome index (EC-1, EC-2) ─────────────────────────

            // Review-workflow index: filter documents by extraction outcome status.
            migrationBuilder.CreateIndex(
                name: "ix_clinical_documents_extraction_outcome",
                table: "clinical_documents",
                column: "ExtractionOutcome");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_clinical_documents_extraction_outcome",
                table: "clinical_documents");

            migrationBuilder.DropColumn(
                name: "ExtractionOutcome",
                table: "clinical_documents");

            migrationBuilder.DropIndex(
                name: "ix_extracted_data_created_at",
                table: "extracted_data");

            migrationBuilder.DropIndex(
                name: "ix_extracted_data_document_id_data_type",
                table: "extracted_data");

            migrationBuilder.DropColumn(
                name: "ExtractionRegion",
                table: "extracted_data");

            migrationBuilder.DropColumn(
                name: "PageNumber",
                table: "extracted_data");
        }
    }
}
